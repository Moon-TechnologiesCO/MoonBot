using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MoonBot.Core;

namespace MoonBot.App;

public partial class MainWindow : Window
{
    private const string UserName = "JD"; // provisional
    private static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BubbleLingers = TimeSpan.FromSeconds(4);

    private readonly MoonStateMachine _machine;
    private readonly DispatcherTimer _typeTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _hideBubbleTimer;
    private string _fullText = "";
    private int _shown;

    // Solo para el diseñador de Avalonia, que necesita un constructor sin parámetros.
    public MainWindow() : this(new MoonStateMachine()) { }

    // La ventana no crea su máquina de estados: se la entregan (inyección por constructor).
    public MainWindow(MoonStateMachine machine)
    {
        _machine = machine;
        InitializeComponent();

        Moon.State = _machine.Current;
        _machine.StateChanged += OnStateChanged;

        _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _typeTimer.Tick += OnTypeTick;

        _idleTimer = new DispatcherTimer { Interval = SleepAfter };
        _idleTimer.Tick += (_, _) => _machine.TryChangeTo(MoonState.Sleeping);

        _hideBubbleTimer = new DispatcherTimer { Interval = BubbleLingers };
        _hideBubbleTimer.Tick += (_, _) =>
        {
            _hideBubbleTimer.Stop();
            BubbleBorder.IsVisible = false;
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
            Say($"Hola, {UserName}. ¿En qué puedo ayudarte hoy?");
        };
    }

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
        AutomationProperties.SetName(Moon, $"MoonBot: {Describe(e.To)}");

        switch (e.To)
        {
            case MoonState.Sleeping:
                BubbleBorder.IsVisible = false;
                break;
            case MoonState.Idle when e.From == MoonState.Sleeping:
                Moon.React();
                Say("¡Ah! Me dormí contando cráteres.");
                break;
            case MoonState.Idle when e.From == MoonState.Error:
                Say("Listo, ya estoy bien.");
                break;
            case MoonState.Thinking:
                Say("Déjame pensar…");
                break;
            case MoonState.Working:
                Say("Manos a la obra.");
                break;
            case MoonState.Error:
                Say("Uy, algo salió mal.");
                break;
        }
    }

    private static string Describe(MoonState state) => state switch
    {
        MoonState.Idle => "lista",
        MoonState.Sleeping => "durmiendo",
        MoonState.Thinking => "pensando",
        MoonState.Working => "trabajando",
        MoonState.Error => "con un error",
        _ => state.ToString()
    };

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
        switch (_machine.Current)
        {
            case MoonState.Sleeping:
            case MoonState.Error:
                _machine.TryChangeTo(MoonState.Idle);
                break;
            case MoonState.Idle:
                Moon.React();
                Say("¡Hey! Aquí estoy.");
                break;
        }
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
        _hideBubbleTimer.Stop();
        _fullText = text;
        _shown = 0;
        Bubble.Text = "";
        BubbleBorder.IsVisible = true;
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
            _hideBubbleTimer.Start();
        }
    }
}
