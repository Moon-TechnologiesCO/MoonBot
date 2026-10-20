using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoonBot.Core;
using MoonBot.Core.Placement;
using PlacementMode = MoonBot.Core.Placement.PlacementMode;

namespace MoonBot.App;

// La luna flotante. Muestra el estado y frases cortas; un clic abre el chat.
public partial class MainWindow : Window
{
    private const int EdgeMargin = 8;
    private static readonly TimeSpan BubbleLingers = TimeSpan.FromSeconds(4);

    private readonly MoonBotHost _host;
    private readonly DispatcherTimer _typeTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _hideBubbleTimer;
    private string _fullText = "";
    private int _shown;

    // Solo para el diseñador de Avalonia.
    public MainWindow() : this(new MoonBotHost()) { }

    public MainWindow(MoonBotHost host)
    {
        _host = host;
        InitializeComponent();

        Moon.State = _host.Machine.Current;
        _host.Machine.StateChanged += OnStateChanged;
        _host.SettingsChanged += (_, _) =>
        {
            _idleTimer!.Interval = SleepAfter;
            ApplyAppearance();
            Place();
        };

        _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _typeTimer.Tick += OnTypeTick;

        _idleTimer = new DispatcherTimer { Interval = SleepAfter };
        _idleTimer.Tick += (_, _) => _host.Machine.TryChangeTo(MoonState.Sleeping);

        _hideBubbleTimer = new DispatcherTimer { Interval = BubbleLingers };
        _hideBubbleTimer.Tick += (_, _) =>
        {
            _hideBubbleTimer.Stop();
            BubbleHost.Opacity = 0;
        };

        PointerMoved += (_, e) =>
        {
            Moon.LookTarget = e.GetPosition(Moon);
            RestartIdleTimer();
        };
        PointerExited += (_, _) => Moon.LookTarget = null;
        PointerPressed += OnPointerPressed;

        // Soltar archivos sobre la luna: se los pasa al chat.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, _) => Moon.React());
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (files is { Count: > 0 } && Chat is not null)
            {
                Chat.Attach(files);
                Chat.ShowNextTo(this);
            }
        });

        ApplyAppearance();

        // Se coloca antes de mostrarse: en Opened la ventana nativa ya existe
        // y Windows ya la puso donde quiso.
        Place();

        // Si cambian las pantallas (se conecta un monitor, cambia la barra de tareas), se recoloca.
        Screens.Changed += (_, _) => Dispatcher.UIThread.Post(Place);

        Opened += (_, _) =>
        {
            RestartIdleTimer();
            var name = _host.Settings.UserName;
            Say(string.IsNullOrWhiteSpace(name)
                ? _host.Text["moon.greetingNoName"]
                : _host.Text.Format("moon.greeting", name));
        };
    }

    // Se asigna desde App: la luna abre y cierra el chat al hacer clic.
    public ChatPopup? Chat { get; set; }

    private TimeSpan SleepAfter => TimeSpan.FromMinutes(Math.Max(1, _host.Settings.SleepAfterMinutes));

    public void ToggleVisible()
    {
        if (IsVisible) Hide();
        else Show();
    }

    private void ApplyAppearance()
    {
        Moon.Skin = MoonSkins.Get(_host.Settings.Skin);
        Moon.Accessory = MoonAccessories.Get(_host.Settings.Accessory);
    }

    // ---------- Posición ----------

    // La pantalla mide en píxeles físicos y la ventana en píxeles lógicos: se multiplica por la escala.
    private void Place()
    {
        var settings = _host.Settings;
        var screen = (settings.Placement == PlacementMode.Custom && settings.CustomX is { } cx && settings.CustomY is { } cy
                         ? Screens.ScreenFromPoint(new PixelPoint(cx, cy))
                         : null)
                     ?? Screens.Primary;
        if (screen is null) return;

        var bounds = new PixelBox(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        var work = new PixelBox(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
        var w = (int)(Width * screen.Scaling);
        var h = (int)(Height * screen.Scaling);

        var (x, y) = settings.Placement == PlacementMode.Custom && settings.CustomX is { } savedX && settings.CustomY is { } savedY
            ? MoonPlacement.Clamp(savedX, savedY, w, h, work)
            : MoonPlacement.Compute(settings.Placement, bounds, work, w, h, EdgeMargin);

        Position = new PixelPoint(x, y);
        SetBubbleBelow(settings.Placement != PlacementMode.Custom && MoonPlacement.BubbleBelow(settings.Placement, bounds, work));
    }

    // Si la luna vive arriba, el globo sale hacia abajo y la colita apunta hacia arriba.
    private void SetBubbleBelow(bool below)
    {
        Grid.SetRow(BubbleHost, below ? 1 : 0);
        Grid.SetRow(Moon, below ? 0 : 1);
        Layout.RowDefinitions = below ? new RowDefinitions("*,Auto") : new RowDefinitions("Auto,*");
        BubbleHost.Margin = below ? new Thickness(12, 0, 12, 12) : new Thickness(12, 12, 12, 0);
        TailUp.IsVisible = below;
        TailDown.IsVisible = !below;
    }

    // ---------- Estados ----------

    // El evento podría llegar desde otro hilo (por ejemplo, una respuesta de la IA).
    // La UI solo se toca desde su propio hilo, así que se encola en el Dispatcher.
    private void OnStateChanged(object? sender, StateChangedEventArgs e)
        => Dispatcher.UIThread.Post(() => ApplyState(e));

    private void ApplyState(StateChangedEventArgs e)
    {
        Moon.State = e.To;
        AutomationProperties.SetName(Moon, $"MoonBot: {e.To}");
        var t = _host.Text;
        var chatOpen = Chat is { IsVisible: true };

        switch (e.To)
        {
            case MoonState.Sleeping:
                BubbleHost.Opacity = 0;
                break;
            case MoonState.Idle when e.From == MoonState.Sleeping:
                Moon.React();
                Say(t["moon.woke"]);
                break;
            case MoonState.Idle when e.From == MoonState.Error:
                Say(t["moon.recovered"]);
                break;
            case MoonState.Idle when e.From == MoonState.Working:
                Moon.React();
                if (!chatOpen)
                    Say(t["moon.done"]);
                break;
            case MoonState.Thinking when !chatOpen:
                Say(t["moon.thinking"]);
                break;
            case MoonState.Working when !chatOpen:
                Say(t["moon.working"]);
                break;
            case MoonState.Error:
                Say(t["moon.error"]);
                break;
        }

        // Si volvió a Idle sin decir nada nuevo, el globo anterior se desvanece a su tiempo.
        if (BubbleHost.Opacity > 0)
            HideBubbleLaterIfIdle();
        RestartIdleTimer();
    }

    // ---------- Ratón ----------

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // En Windows, BeginMoveDrag espera hasta soltar el botón.
        // Si la ventana no se movió, fue un clic; si se movió, se recuerda el sitio.
        var before = Position;
        BeginMoveDrag(e);
        if (Position == before)
            OnMoonClicked();
        else
            _host.Update(s => s with { Placement = PlacementMode.Custom, CustomX = Position.X, CustomY = Position.Y });

        RestartIdleTimer();
    }

    private void OnMoonClicked()
    {
        if (_host.Machine.Current is MoonState.Sleeping or MoonState.Error)
            _host.Machine.TryChangeTo(MoonState.Idle);
        else
            Moon.React();

        BubbleHost.Opacity = 0;
        Chat?.Toggle(this);
    }

    private void RestartIdleTimer()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    // ---------- Globo ----------

    public void Say(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _hideBubbleTimer.Stop();
        _fullText = text;
        _shown = 0;
        Bubble.Text = "";
        BubbleHost.Opacity = 1;
        Moon.IsTalking = true;
        _typeTimer.Start();
    }

    private void OnTypeTick(object? sender, EventArgs e)
    {
        _shown++;
        Bubble.Text = _fullText[.._shown];

        if (_shown >= _fullText.Length)
        {
            _typeTimer.Stop();
            Moon.IsTalking = false;
            HideBubbleLaterIfIdle();
        }
    }

    // Mientras piensa o escribe, el globo se queda: así siempre se ve que está ocupada.
    private void HideBubbleLaterIfIdle()
    {
        if (_typeTimer.IsEnabled || _host.Machine.Current is MoonState.Thinking or MoonState.Working)
            return;

        _hideBubbleTimer.Stop();
        _hideBubbleTimer.Start();
    }
}
