using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MoonBot.App;

public class MoonView : Control
{
    private static readonly IBrush BodyBrush = new SolidColorBrush(Color.Parse("#E8E4D4"));
    private static readonly IBrush CraterBrush = new SolidColorBrush(Color.Parse("#CFCAB5"));
    private static readonly IBrush EyeBrush = new SolidColorBrush(Color.Parse("#1B2140"));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _frameTimer;

    public bool IsTalking { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _frameTimer.Tick += (_, _) => InvalidateVisual();
        _frameTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frameTimer?.Stop();
        _frameTimer = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var t = _clock.Elapsed.TotalSeconds;
        var breathe = 1 + 0.02 * Math.Sin(t * 2 * Math.PI / 3.5);

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = Math.Min(Bounds.Width, Bounds.Height) * 0.35 * breathe;

        context.DrawEllipse(BodyBrush, null, center, r, r);

        DrawCrater(context, center, r, -0.45, -0.50, 0.16);
        DrawCrater(context, center, r, 0.50, -0.30, 0.11);
        DrawCrater(context, center, r, 0.35, 0.55, 0.18);

        var eyeOpen = EyeOpenness(t);
        DrawEye(context, center, r, -0.30, eyeOpen);
        DrawEye(context, center, r, 0.30, eyeOpen);

        DrawMouth(context, center, r, MouthOpenness(t));
    }

    // Cada 4 segundos parpadea durante 0.2 s: 1 (abierto) -> 0 (cerrado) -> 1
    private static double EyeOpenness(double t)
    {
        var phase = t % 4.0;
        if (phase < 3.8) return 1.0;
        var x = (phase - 3.8) / 0.2;
        return Math.Abs(2 * x - 1);
    }

    // Hablando: la boca oscila rápido. Callada: casi cerrada.
    private double MouthOpenness(double t)
        => IsTalking ? 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(t * 16)) : 0.0;

    private static void DrawCrater(DrawingContext context, Point center, double r,
        double dx, double dy, double size)
    {
        var p = new Point(center.X + dx * r, center.Y + dy * r);
        context.DrawEllipse(CraterBrush, null, p, size * r, size * r);
    }

    private static void DrawEye(DrawingContext context, Point center, double r,
        double dx, double openness)
    {
        var p = new Point(center.X + dx * r, center.Y - 0.05 * r);
        context.DrawEllipse(EyeBrush, null, p, 0.09 * r, 0.09 * r * Math.Max(openness, 0.08));
    }

    private static void DrawMouth(DrawingContext context, Point center, double r, double openness)
    {
        var p = new Point(center.X, center.Y + 0.32 * r);
        context.DrawEllipse(EyeBrush, null, p, 0.13 * r, (0.015 + 0.09 * openness) * r);
    }
}