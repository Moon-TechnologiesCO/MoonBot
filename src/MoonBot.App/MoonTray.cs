using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoonBot.Core;

namespace MoonBot.App;

// Ícono de la bandeja del sistema con su menú.
public static class MoonTray
{
    public static TrayIcon Create(MainWindow window, MoonStateMachine machine, Action quit)
    {
        var menu = new NativeMenu();

        menu.Items.Add(Item("Mostrar / ocultar", window.ToggleVisible));
        menu.Items.Add(Item("Dormir", () => machine.TryChangeTo(MoonState.Sleeping)));
        menu.Items.Add(Item("Despertar", () => machine.TryChangeTo(MoonState.Idle)));

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
                    window.Say($"No puedo pasar de {from} a {state}.");
            }));
        }
        menu.Items.Add(new NativeMenuItem("Probar estado (debug)") { Menu = states });
#endif

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item("Salir", quit));

        var tray = new TrayIcon
        {
            Icon = CreateIcon(),
            ToolTipText = "MoonBot",
            Menu = menu
        };
        tray.Clicked += (_, _) => window.ToggleVisible();
        return tray;
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
