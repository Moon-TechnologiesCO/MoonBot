using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;

namespace MoonBot.App;

// Base para todo lo que aparece en el chat. Avisa (INotifyPropertyChanged) cuando algo cambia,
// así la interfaz se actualiza sola (por ejemplo, el texto que llega por streaming).
public abstract class ChatEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        Changed(name!);
        return true;
    }

    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Un mensaje tuyo o de MoonBot.
public sealed class MessageEntry : ChatEntry
{
    private static readonly IBrush UserBackground = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#9483FF"), 0), new GradientStop(Color.Parse("#6C58E6"), 1) }
    };
    private static readonly IBrush BotBackground = new SolidColorBrush(Color.Parse("#17FFFFFF"));
    private static readonly IBrush ErrorBackground = new SolidColorBrush(Color.Parse("#33FF6B6B"));
    private static readonly IBrush BotBorder = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly IBrush ErrorBorder = new SolidColorBrush(Color.Parse("#66FF8A8A"));
    private static readonly IBrush UserText = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush BotText = new SolidColorBrush(Color.Parse("#F3F1FF"));
    private static readonly IBrush PendingText = new SolidColorBrush(Color.Parse("#A9A6CF"));

    private string _text;
    private bool _isPending;
    private bool _isError;

    private MessageEntry(bool isUser, string text, bool pending)
    {
        IsUser = isUser;
        _text = text;
        _isPending = pending;
    }

    public static MessageEntry User(string text) => new(true, text, false);
    public static MessageEntry Bot(string text, bool pending = false) => new(false, text, pending);

    public bool IsUser { get; }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool IsPending
    {
        get => _isPending;
        set { if (Set(ref _isPending, value)) Changed(nameof(Foreground)); }
    }

    public bool IsError
    {
        get => _isError;
        set
        {
            if (!Set(ref _isError, value)) return;
            Changed(nameof(Background));
            Changed(nameof(BorderBrush));
        }
    }

    public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public IBrush Background => IsUser ? UserBackground : IsError ? ErrorBackground : BotBackground;
    public IBrush BorderBrush => IsUser ? Brushes.Transparent : IsError ? ErrorBorder : BotBorder;
    public IBrush Foreground => IsUser ? UserText : IsPending ? PendingText : BotText;

    // La esquina "pegada" a quien habla queda casi recta, como en las apps de mensajería.
    public CornerRadius Corners => IsUser ? new CornerRadius(18, 18, 6, 18) : new CornerRadius(18, 18, 18, 6);
}

// Línea pequeña de actividad: "Buscando…", "Movidos 3 archivos", avisos.
public sealed class NoticeEntry(string text, bool isWarning = false) : ChatEntry
{
    private string _text = text;

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public IBrush Foreground => isWarning
        ? new SolidColorBrush(Color.Parse("#F5C46B"))
        : new SolidColorBrush(Color.Parse("#A9A6CF"));
}

// Un archivo o carpeta encontrado, con botones para abrirlo.
public sealed class FileCard
{
    public FileCard(string path, Action<string> open, Action<string> reveal, string openText, string revealText)
    {
        Path = path;
        OpenText = openText;
        RevealText = revealText;
        IsFolder = Directory.Exists(path);
        Name = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(path) ?? "";
        OpenCommand = new RelayCommand(() => open(path));
        RevealCommand = new RelayCommand(() => reveal(path));
    }

    public string Path { get; }
    public string Name { get; }
    public string Folder { get; }
    public bool IsFolder { get; }
    public string OpenText { get; }
    public string RevealText { get; }
    public string Glyph => IsFolder ? "📁" : GlyphFor(System.IO.Path.GetExtension(Path));
    public ICommand OpenCommand { get; }
    public ICommand RevealCommand { get; }

    private static string GlyphFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "📕",
        ".doc" or ".docx" or ".txt" or ".md" or ".rtf" => "📄",
        ".xls" or ".xlsx" or ".csv" => "📊",
        ".ppt" or ".pptx" => "📽",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".heic" => "🖼",
        ".mp3" or ".wav" or ".flac" or ".m4a" => "🎵",
        ".mp4" or ".mov" or ".mkv" or ".avi" => "🎬",
        ".zip" or ".rar" or ".7z" => "🗜",
        _ => "📎"
    };
}

public sealed class FilesEntry(string title, IReadOnlyList<FileCard> files) : ChatEntry
{
    public string Title { get; } = title;
    public IReadOnlyList<FileCard> Files { get; } = files;
}

// Tarjeta "MoonBot quiere hacer esto. ¿Permitir?". Decision se completa al pulsar un botón.
public sealed class ApprovalEntry : ChatEntry
{
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _result;

    public ApprovalEntry(string title, IReadOnlyList<string> items, string? target, string allowText, string denyText)
    {
        Title = title;
        Items = items;
        Target = target;
        AllowText = allowText;
        DenyText = denyText;
        AllowCommand = new RelayCommand(() => Decide(true));
        DenyCommand = new RelayCommand(() => Decide(false));
    }

    public string Title { get; }
    public IReadOnlyList<string> Items { get; }
    public string? Target { get; }
    public bool HasTarget => Target is not null;
    public string AllowText { get; }
    public string DenyText { get; }
    public ICommand AllowCommand { get; }
    public ICommand DenyCommand { get; }
    public Task<bool> Decision => _decision.Task;

    public bool IsPending => _result is null;

    // Texto que reemplaza a los botones cuando ya se decidió ("Permitido" / "Denegado").
    public string? Result
    {
        get => _result;
        private set
        {
            if (Set(ref _result, value))
                Changed(nameof(IsPending));
        }
    }

    public string ResolvedAllowedText { get; init; } = "Allowed";
    public string ResolvedDeniedText { get; init; } = "Denied";

    public void Decide(bool allowed)
    {
        if (!_decision.TrySetResult(allowed))
            return;
        Result = allowed ? "✓ " + ResolvedAllowedText : "✕ " + ResolvedDeniedText;
    }
}

// ICommand mínimo: ejecuta una acción. Es lo que los botones de las plantillas usan con {Binding}.
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
