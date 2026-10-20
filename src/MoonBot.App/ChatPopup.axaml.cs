using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoonBot.Core;
using MoonBot.Core.Chat;
using MoonBot.Core.Placement;
using MoonBot.Core.Tools;
using MoonBot.Infrastructure.Tools;

namespace MoonBot.App;

// El chat compacto que sale junto a la luna. Aquí vive la conversación, el comando "/find",
// las tarjetas de archivos, las tarjetas de permiso y los archivos arrastrados.
public partial class ChatPopup : Window
{
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#7FE0A0"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#F5C46B"));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#FF8A8A"));
    private static readonly IBrush BlueBrush = new SolidColorBrush(Color.Parse("#A99BFF"));
    private static readonly IBrush SleepBrush = new SolidColorBrush(Color.Parse("#6B739A"));

    private readonly MoonBotHost _host;
    private readonly ObservableCollection<ChatEntry> _items = [];
    private readonly List<string> _attachments = [];
    private CancellationTokenSource? _replyCts;

    // Solo para el diseñador de Avalonia.
    public ChatPopup() : this(new MoonBotHost()) { }

    public ChatPopup(MoonBotHost host)
    {
        _host = host;
        InitializeComponent();

        Messages.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => EmptyState.IsVisible = _items.Count == 0;

        SendButton.Click += async (_, _) => await SendAsync(Input.Text);
        StopButton.Click += (_, _) => _replyCts?.Cancel();
        NewChatButton.Click += (_, _) => NewChat();
        HubButton.Click += (_, _) => HubRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Click += (_, _) => Hide();
        ClearAttachButton.Click += (_, _) => { _attachments.Clear(); UpdateAttachBar(); };

        ActionFind.Click += (_, _) => Prefill("/find ");
        ChipFind.Click += (_, _) => Prefill("/find ");
        ActionOrganize.Click += (_, _) => Prefill(_host.Text["prompt.organize"]);
        ChipOrganize.Click += (_, _) => Prefill(_host.Text["prompt.organize"]);
        ChipDownloads.Click += async (_, _) => await SendAsync(_host.Text["prompt.downloads"]);
        ActionAsk.Click += (_, _) => Input.Focus();

        // Enter envía; Shift+Enter hace salto de línea; Esc cierra.
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };

        // La cabecera sirve para mover la ventana.
        Header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        // Arrastrar archivos desde el Explorador.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => { DropOverlay.IsVisible = HasFiles(e); });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropOverlay.IsVisible = false);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            DropOverlay.IsVisible = false;
            Attach(FilesFrom(e));
        });

        // Cerrar con la X del sistema (Alt+F4) solo la oculta.
        Closing += (_, e) =>
        {
            if (e.CloseReason is WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        _host.Approvals.Handler = RequestApprovalAsync;
        _host.Machine.StateChanged += (_, e) => Dispatcher.UIThread.Post(() => ShowState(e.To));
        _host.SettingsChanged += (_, _) => ApplySettings();

        ApplySettings();
        ShowState(_host.Machine.Current);
    }

    public event EventHandler? HubRequested;

    // La luna pide permiso: se usa para que ella "diga" algo.
    public Action<string>? MoonSays { get; set; }

    // ---------- Mostrar junto a la luna ----------

    public void ShowNextTo(Window moon)
    {
        var screen = moon.Screens.ScreenFromWindow(moon) ?? moon.Screens.Primary;
        if (screen is not null)
        {
            var scale = screen.Scaling;
            var area = screen.WorkingArea;
            var moonBox = new PixelBox(moon.Position.X, moon.Position.Y,
                (int)(moon.Width * scale), (int)(moon.Height * scale));
            var (x, y) = MoonPlacement.PopupNextTo(moonBox, (int)(Width * scale), (int)(Height * scale),
                new PixelBox(area.X, area.Y, area.Width, area.Height), 0);
            Position = new PixelPoint(x, y);
        }

        Show();
        Activate();
        Dispatcher.UIThread.Post(() => Input.Focus());
    }

    public void Toggle(Window moon)
    {
        if (IsVisible) Hide();
        else ShowNextTo(moon);
    }

    // ---------- Textos y apariencia ----------

    private void ApplySettings()
    {
        var t = _host.Text;
        MiniMoon.Skin = MoonSkins.Get(_host.Settings.Skin);
        MiniMoon.Accessory = MoonAccessories.Get(_host.Settings.Accessory);

        GreetingText.Text = Greeting();
        EmptyBody.Text = t["chat.emptyBody"];
        ActionFindTitle.Text = t["action.find.title"];
        ActionFindBody.Text = t["action.find.body"];
        ActionOrganizeTitle.Text = t["action.organize.title"];
        ActionOrganizeBody.Text = t["action.organize.body"];
        ActionAskTitle.Text = t["action.ask.title"];
        ActionAskBody.Text = t["action.ask.body"];
        ActionDropTitle.Text = t["action.drop.title"];
        ActionDropBody.Text = t["action.drop.body"];
        ChipFind.Content = t["chip.find"];
        ChipDownloads.Content = t["chip.downloads"];
        ChipOrganize.Content = t["chip.organize"];
        Input.PlaceholderText = t["chat.placeholder"];
        DropText.Text = t["drop.here"];

        // Nombres para lectores de pantalla en botones de solo icono.
        AutomationProperties.SetName(NewChatButton, t["chat.new"]);
        AutomationProperties.SetName(HubButton, t["nav.settings"]);
        AutomationProperties.SetName(CloseButton, t["chat.close"]);
        AutomationProperties.SetName(SendButton, t["chat.send"]);
        AutomationProperties.SetName(StopButton, t["chat.stop"]);
        AutomationProperties.SetName(Input, t["chat.title"]);
        ToolTip.SetTip(NewChatButton, t["chat.new"]);
        ToolTip.SetTip(HubButton, t["nav.settings"]);

        UpdateAttachBar();
        ShowState(_host.Machine.Current);
    }

    private string Greeting()
    {
        var name = string.IsNullOrWhiteSpace(_host.Settings.UserName) ? "" : ", " + _host.Settings.UserName.Trim();
        var key = DateTime.Now.Hour switch
        {
            >= 5 and < 12 => "chat.greeting.morning",
            >= 12 and < 19 => "chat.greeting.afternoon",
            _ => "chat.greeting.evening"
        };
        return _host.Text.Format(key, name);
    }

    private void ShowState(MoonState state)
    {
        MiniMoon.State = state;
        var t = _host.Text;
        (StatusText.Text, StatusDot.Fill) = state switch
        {
            MoonState.Thinking => (t["status.thinking"], BlueBrush),
            MoonState.Working => (t["status.working"], WarnBrush),
            MoonState.Sleeping => (t["status.sleeping"], SleepBrush),
            MoonState.Error => (t["status.error"], BadBrush),
            _ => (t["status.ready"] + " · " + _host.Settings.Model, OkBrush)
        };
    }

    // ---------- Enviar ----------

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            _ = SendAsync(Input.Text);
        }
    }

    private void Prefill(string text)
    {
        Input.Text = text;
        Input.CaretIndex = text.Length;
        Input.Focus();
    }

    public async Task SendAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || _host.Chat.IsBusy)
            return;

        Input.Text = "";
        var trimmed = text.Trim();

        // Comando rápido: búsqueda instantánea sin IA.
        foreach (var command in new[] { "/find ", "/buscar " })
        {
            if (trimmed.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            {
                await QuickFindAsync(trimmed[command.Length..].Trim());
                return;
            }
        }

        _items.Add(MessageEntry.User(trimmed + AttachmentNote(forDisplay: true)));
        var prompt = trimmed + AttachmentNote(forDisplay: false);
        _attachments.Clear();
        UpdateAttachBar();

        var reply = MessageEntry.Bot(_host.Text["moon.thinking"], pending: true);
        _items.Add(reply);
        SetBusy(true);
        ScrollToEnd();

        var observer = new ChatObserver
        {
            OnText = chunk => OnUi(() =>
            {
                if (reply.IsPending)
                {
                    reply.Text = "";
                    reply.IsPending = false;
                }
                reply.Text += chunk;
                ScrollToEnd();
            }),
            OnToolStarted = call => OnUi(() => InsertBefore(reply, new NoticeEntry(ToolActivity(call.Name)))),
            OnToolFinished = (call, outcome) => OnUi(() => ShowToolOutcome(reply, call, outcome)),
            OnToolsUnavailable = () => OnUi(() => InsertBefore(reply, new NoticeEntry(_host.Text["tools.unavailable"], isWarning: true)))
        };

        _replyCts = new CancellationTokenSource();
        try
        {
            await _host.Chat.SendAsync(prompt, observer, _replyCts.Token);
            if (reply.IsPending) // terminó solo con herramientas, sin texto
                _items.Remove(reply);
        }
        catch (OperationCanceledException)
        {
            if (reply.IsPending)
                _items.Remove(reply);
            else
                reply.Text += "  " + _host.Text["chat.stopped"];
        }
        catch (Exception e)
        {
            // Se registra el tipo de error, nunca el contenido de la conversación.
            _host.Log.Error("Chat request failed", e);
            reply.IsPending = false;
            reply.IsError = true;
            reply.Text = _host.Text.Format("chat.failed", _host.Describe(e));
        }
        finally
        {
            _replyCts.Dispose();
            _replyCts = null;
            SetBusy(false);
            ScrollToEnd();
            Input.Focus();
        }
    }

    private async Task QuickFindAsync(string query)
    {
        if (query.Length == 0)
            return;

        var t = _host.Text;
        _items.Add(MessageEntry.User("🔍 " + query));
        var notice = new NoticeEntry(t.Format("find.searching", query));
        _items.Add(notice);
        ScrollToEnd();

        var results = await _host.QuickSearchAsync(query, CancellationToken.None);
        _items.Remove(notice);
        if (results.Count == 0)
            _items.Add(new NoticeEntry(t.Format("find.none", query)));
        else
            _items.Add(new FilesEntry(t.Format("find.results", results.Count, query), results.Select(ToCard).ToList()));
        _host.Log.Info($"Quick find: {results.Count} results");
        ScrollToEnd();
    }

    private void NewChat()
    {
        _replyCts?.Cancel();
        _host.Chat.Clear();
        _items.Clear();
        _attachments.Clear();
        UpdateAttachBar();
        GreetingText.Text = Greeting();
        Input.Focus();
    }

    private void SetBusy(bool busy)
    {
        SendButton.IsVisible = !busy;
        StopButton.IsVisible = busy;
    }

    private void InsertBefore(ChatEntry anchor, ChatEntry entry)
    {
        var index = _items.IndexOf(anchor);
        _items.Insert(index < 0 ? _items.Count : index, entry);
        ScrollToEnd();
    }

    private void ScrollToEnd() => Dispatcher.UIThread.Post(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Background);

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    // ---------- Herramientas ----------

    private string ToolActivity(string toolName)
    {
        var key = "tool.running." + toolName;
        var text = _host.Text[key];
        return text == key ? "⚙️ " + toolName : text;
    }

    private void ShowToolOutcome(ChatEntry anchor, ToolCall call, ToolOutcome outcome)
    {
        var t = _host.Text;
        if (outcome.Denied)
            InsertBefore(anchor, new NoticeEntry(t["tool.denied"], isWarning: true));
        else if (!outcome.Ok)
            InsertBefore(anchor, new NoticeEntry(t.Format("tool.failed", ErrorText(outcome.Json)), isWarning: true));
        else if (outcome.Paths.Count > 0)
            InsertBefore(anchor, new FilesEntry(t["tool.result." + call.Name] is var title && title != "tool.result." + call.Name
                ? title : call.Name, outcome.Paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(ToCard).ToList()));
    }

    private static string ErrorText(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? json : json;
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }

    // Lo llama el ToolRouter (a través de UiApprovalGate) antes de cualquier cambio.
    private async Task<bool> RequestApprovalAsync(ToolCall call, ToolPreview preview, CancellationToken cancellationToken)
    {
        var entry = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var t = _host.Text;
            var title = preview.ActionKey switch
            {
                "tool.move" => t.Format("approval.move", preview.Items.Count),
                "tool.rename" => t["approval.rename"],
                "tool.createFolder" => t["approval.createFolder"],
                _ => preview.ActionKey
            };
            var card = new ApprovalEntry(title, preview.Items, preview.Target, t["approval.allow"], t["approval.deny"])
            {
                ResolvedAllowedText = t["approval.allowed"],
                ResolvedDeniedText = t["approval.denied"]
            };
            _items.Add(card);
            ScrollToEnd();
            if (!IsVisible)
                MoonSays?.Invoke(t["moon.needsOk"]);
            return card;
        });

        // Si se cancela o se acaba el tiempo, la tarjeta queda como "Denegado".
        await using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => entry.Decide(false)));
        return await entry.Decision;
    }

    // ---------- Archivos: abrir, mostrar, adjuntar ----------

    private FileCard ToCard(string path)
        => new(path, OpenPath, RevealPath, _host.Text["files.open"], _host.Text["files.reveal"]);

    private async void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
                await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            else if (FileSandbox.ExecutableExtensions.Contains(Path.GetExtension(path)))
                RevealPath(path); // un ejecutable nunca se abre desde aquí: solo se muestra
            else
                await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception e)
        {
            _host.Log.Error("Could not open a file", e);
        }
    }

    private void RevealPath(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            info.ArgumentList.Add("/select," + path);
            Process.Start(info);
        }
        else if (Path.GetDirectoryName(path) is { } folder)
        {
            _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        }
    }

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.TryGetFiles() is { Length: > 0 };

    private static IEnumerable<string> FilesFrom(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? [];

    // El dictado puede mandar el texto aquí en vez de escribirlo en otra app.
    public void AppendToInput(string text)
    {
        Input.Text = string.IsNullOrWhiteSpace(Input.Text) ? text : Input.Text.TrimEnd() + " " + text;
        Input.CaretIndex = Input.Text.Length;
        Input.Focus();
    }

    public void Attach(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!_attachments.Contains(path, StringComparer.OrdinalIgnoreCase))
                _attachments.Add(path);
        UpdateAttachBar();
        Input.Focus();
    }

    private void UpdateAttachBar()
    {
        AttachBar.IsVisible = _attachments.Count > 0;
        AttachText.Text = _attachments.Count == 1
            ? "📎 " + Path.GetFileName(_attachments[0])
            : _host.Text.Format("attach.count", _attachments.Count);
    }

    // Los adjuntos viajan al modelo como una lista de rutas; en pantalla solo se ven los nombres.
    private string AttachmentNote(bool forDisplay)
    {
        if (_attachments.Count == 0)
            return "";
        var builder = new StringBuilder("\n\n");
        if (forDisplay)
            builder.Append("📎 ").AppendJoin(", ", _attachments.Select(Path.GetFileName));
        else
        {
            builder.Append("[Files the user attached]\n");
            foreach (var path in _attachments)
                builder.Append("- ").Append(path).Append('\n');
        }
        return builder.ToString();
    }
}
