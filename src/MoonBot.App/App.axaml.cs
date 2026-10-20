using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

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
            var host = new MoonBotHost();
            var icon = MoonTray.CreateIcon();
            var moon = new MainWindow(host) { Icon = icon };
            var panel = new PanelWindow(host) { Icon = icon };
            moon.Panel = panel;

            // La luna no tiene botón de cerrar y el panel solo se oculta: se sale desde la bandeja.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = moon;
            desktop.Exit += (_, _) => host.Dispose();

            var tray = MoonTray.Create(host, moon, panel, () => desktop.Shutdown());
            TrayIcon.SetIcons(this, [tray]);

            // Primera vez: se abre el panel para que el usuario vea cómo empezar.
            if (host.IsFirstRun)
                moon.Opened += (_, _) => panel.Open(PanelPage.Chat);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
