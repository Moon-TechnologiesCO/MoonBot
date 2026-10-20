using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoonBot.Core;

namespace MoonBot.App;

// Ícono de la bandeja del sistema con su menú. El menú se reconstruye al cambiar de idioma.
public static class MoonTray
{
    public static TrayIcon Create(MoonBotHost host, MainWindow moon, PanelWindow panel, Action quit)
    {
        var tray = new TrayIcon
        {
            Icon = CreateIcon(),
            ToolTipText = "MoonBot",
            Menu = BuildMenu(host, moon, panel, quit)
        };
        tray.Clicked += (_, _) => panel.Open(PanelPage.Chat);
        host.SettingsChanged += (_, _) => tray.Menu = BuildMenu(host, moon, panel, quit);
        return tray;
    }

    private static NativeMenu BuildMenu(MoonBotHost host, MainWindow moon, PanelWindow panel, Action quit)
    {
        var t = host.Text;
        var machine = host.Machine;
        var menu = new NativeMenu();

        menu.Items.Add(Item(t["tray.open"], () => panel.Open(PanelPage.Chat)));
        menu.Items.Add(Item(t["nav.settings"], () => panel.Open(PanelPage.Settings)));
        menu.Items.Add(Item(t["tray.toggleMoon"], moon.ToggleVisible));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(t["tray.sleep"], () => machine.TryChangeTo(MoonState.Sleeping)));
        menu.Items.Add(Item(t["tray.wake"], () => machine.TryChangeTo(MoonState.Idle)));

#if DEBUG
        // Solo en Debug: forzar estados para probar las animaciones.
        // Si la transición no está en la tabla, MoonBot lo dice en vez de cambiar.
        var states = new NativeMenu();
        foreach (var state in Enum.GetValues<MoonState>())
        {
            states.Items.Add(Item(state.ToString(), () =>
            {
                var from = machine.Current;
                if (!machine.TryChangeTo(state))
                {
                    moon.Say(t.Format("moon.cantChange", from, state));
                    host.Log.Info($"Rejected transition {from} -> {state}");
                }
            }));
        }
        menu.Items.Add(new NativeMenuItem(t["tray.testState"]) { Menu = states });
#endif

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(t["tray.quit"], quit));
        return menu;
    }

    private static NativeMenuItem Item(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    // Dibuja una luna creciente de 64x64 y la convierte en ícono.
    public static WindowIcon CreateIcon()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
        using (var context = bitmap.CreateDrawingContext())
        {
            var body = new EllipseGeometry(new Rect(4, 4, 56, 56));
            context.DrawGeometry(new SolidColorBrush(Color.Parse("#E8E4D4")), null, body);
            using (context.PushGeometryClip(body))
            {
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#CFCAB5")), null, new Point(22, 20), 6, 6);
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#1B2140")), null, new Point(52, 26), 26, 26);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
