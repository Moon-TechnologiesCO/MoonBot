using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MoonBot.App;

public partial class MainWindow : Window
{
    private const string UserName = "JD"; // provisional
    private readonly DispatcherTimer _typeTimer;
    private string _fullText = "";
    private int _shown;

    public MainWindow()
    {
        InitializeComponent();

        _typeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _typeTimer.Tick += OnTypeTick;

        Opened += (_, _) => Say($"Hola, {UserName}. ¿En qué puedo ayudarte hoy?");
    }

    private void Say(string text)
    {
        _fullText = text;
        _shown = 0;
        Bubble.Text = "";
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
        }
    }
}