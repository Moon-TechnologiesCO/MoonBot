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
            var chat = new ChatPopup(host) { Icon = icon };
            var hub = new HubWindow(host) { Icon = icon };

            moon.Chat = chat;
            chat.MoonSays = moon.Say;
            chat.HubRequested += (_, _) => hub.Open(HubPage.General);
            hub.ChatRequested += (_, _) => chat.ShowNextTo(moon);

            // La luna no tiene botón de cerrar y las demás ventanas solo se ocultan: se sale desde la bandeja.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = moon;
            desktop.Exit += (_, _) => host.Dispose();

            var tray = MoonTray.Create(host, moon, chat, hub, () => desktop.Shutdown());
            TrayIcon.SetIcons(this, [tray]);

            // Primera vez: se abre el chat para que el usuario vea cómo empezar.
            if (host.IsFirstRun)
                moon.Opened += (_, _) => chat.ShowNextTo(moon);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
