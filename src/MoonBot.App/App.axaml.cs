using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MoonBot.Core;

namespace MoonBot.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Raíz de composición: el único lugar donde se crean y conectan las piezas.
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var machine = new MoonStateMachine();
            var window = new MainWindow(machine) { Icon = MoonTray.CreateIcon() };

            // La ventana no tiene botón de cerrar: se sale desde la bandeja.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = window;

            var tray = MoonTray.Create(window, machine, () => desktop.Shutdown());
            TrayIcon.SetIcons(this, [tray]);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
