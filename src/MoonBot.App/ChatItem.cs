using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;

namespace MoonBot.App;

// Un mensaje tal como se muestra en el chat. Avisa (INotifyPropertyChanged) cuando su texto cambia,
// así la burbuja crece mientras la respuesta llega por streaming.
public sealed class ChatItem : INotifyPropertyChanged
{
    private static readonly IBrush UserBackground = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#3D4FB0"), 0), new GradientStop(Color.Parse("#2E3C8E"), 1) }
    };
    private static readonly IBrush BotBackground = new SolidColorBrush(Color.Parse("#171F48"));
    private static readonly IBrush ErrorBackground = new SolidColorBrush(Color.Parse("#3A1830"));
    private static readonly IBrush BotBorder = new SolidColorBrush(Color.Parse("#262F63"));
    private static readonly IBrush ErrorBorder = new SolidColorBrush(Color.Parse("#7A3355"));
    private static readonly IBrush UserText = new SolidColorBrush(Color.Parse("#F4F2FF"));
    private static readonly IBrush BotText = new SolidColorBrush(Color.Parse("#ECE8DA"));
    private static readonly IBrush PendingText = new SolidColorBrush(Color.Parse("#9AA3C7"));

    private string _text;
    private bool _isPending;
    private bool _isError;

    private ChatItem(bool isUser, string text, bool isPending)
    {
        IsUser = isUser;
        _text = text;
        _isPending = isPending;
    }

    public static ChatItem User(string text) => new(true, text, false);
    public static ChatItem Bot(string text, bool pending = false) => new(false, text, pending);

    public event PropertyChangedEventHandler? PropertyChanged;

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
    public CornerRadius Corners => IsUser ? new CornerRadius(18, 18, 4, 18) : new CornerRadius(18, 18, 18, 4);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        Changed(name!);
        return true;
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
