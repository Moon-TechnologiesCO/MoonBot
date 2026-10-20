using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MoonBot.Core;

namespace MoonBot.App;

// La luna flotante. Muestra el estado y frases cortas; un clic abre el panel.
public partial class MainWindow : Window
{
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
        _host.SettingsChanged += (_, _) => _idleTimer!.Interval = SleepAfter;

        _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
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

        // Se coloca antes de mostrarse: en Opened la ventana nativa ya existe
        // y Windows ya la puso donde quiso.
        PlaceInCorner();

        Opened += (_, _) =>
        {
            RestartIdleTimer();
            var name = _host.Settings.UserName;
            Say(string.IsNullOrWhiteSpace(name)
                ? _host.Text["moon.greetingNoName"]
                : _host.Text.Format("moon.greeting", name));
        };
    }

    // Se asigna desde App: la luna abre el panel al hacer clic.
    public PanelWindow? Panel { get; set; }

    private TimeSpan SleepAfter => TimeSpan.FromMinutes(Math.Max(1, _host.Settings.SleepAfterMinutes));

    public void ToggleVisible()
    {
        if (IsVisible) Hide();
        else Show();
    }

    // El evento podría llegar desde otro hilo (por ejemplo, una respuesta de la IA).
    // La UI solo se toca desde su propio hilo, así que se encola en el Dispatcher.
    private void OnStateChanged(object? sender, StateChangedEventArgs e)
        => Dispatcher.UIThread.Post(() => ApplyState(e));

    private void ApplyState(StateChangedEventArgs e)
    {
        Moon.State = e.To;
        AutomationProperties.SetName(Moon, $"MoonBot: {e.To}");
        var t = _host.Text;

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
                if (Panel is not { IsActive: true })
                    Say(t["moon.done"]);
                break;
            case MoonState.Thinking:
                Say(t["moon.thinking"]);
                break;
            case MoonState.Working:
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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // En Windows, BeginMoveDrag espera hasta soltar el botón.
        // Si la ventana no se movió, lo tratamos como un clic.
        var before = Position;
        BeginMoveDrag(e);
        if (Position == before)
            OnMoonClicked();

        RestartIdleTimer();
    }

    private void OnMoonClicked()
    {
        if (_host.Machine.Current is MoonState.Sleeping or MoonState.Error)
            _host.Machine.TryChangeTo(MoonState.Idle);
        else
            Moon.React();

        Panel?.Open(PanelPage.Chat);
    }

    private void RestartIdleTimer()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    // Esquina inferior derecha del área útil (sin la barra de tareas).
    // La pantalla mide en píxeles físicos y la ventana en píxeles lógicos: se multiplica por la escala.
    private void PlaceInCorner()
    {
        var screen = Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        var w = (int)(Width * screen.Scaling);
        var h = (int)(Height * screen.Scaling);
        Position = new PixelPoint(area.Right - w - 16, area.Bottom - h - 16);
    }

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
