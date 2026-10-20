using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MoonBot.Core;

namespace MoonBot.App;

// Dibuja a MoonBot. No decide nada: recibe un estado, una skin y un accesorio, y los pinta.
public class MoonView : Control
{
    private static readonly IBrush ZBrush = new SolidColorBrush(Color.Parse("#C9C2FF"));
    private static readonly IBrush White = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#FFD86B"));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _frameTimer;
    private double _lastT;

    // Valores "suavizados": persiguen a su objetivo un poco en cada frame,
    // así un cambio de estado se ve como una transición y no como un salto.
    private double _shadow = ShadowTarget(MoonState.Idle);
    private Vector _look;
    private double _reactStart = double.NegativeInfinity;

    public MoonState State { get; set; } = MoonState.Idle;
    public bool IsTalking { get; set; }
    public MoonSkin Skin { get; set; } = MoonSkins.All[0];
    public string Accessory { get; set; } = "none";

    // Posición del cursor en coordenadas de este control, o null si no está encima.
    public Point? LookTarget { get; set; }

    // Pequeño salto de reacción (por ejemplo, al hacer clic).
    public void React() => _reactStart = _clock.Elapsed.TotalSeconds;

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
        var dt = Math.Min(t - _lastT, 0.1);
        _lastT = t;

        var baseR = Math.Min(Bounds.Width, Bounds.Height) * 0.30;
        var breathePeriod = State == MoonState.Sleeping ? 6.0 : 3.5;
        var r = baseR * (1 + 0.02 * Math.Sin(t * 2 * Math.PI / breathePeriod));
        var center = new Point(Bounds.Width / 2, Bounds.Height * 0.56 - HopHeight(t) * baseR);

        // Suavizado exponencial: cada frame recorre una fracción de lo que falta.
        // Depende de dt, así se ve igual a 30 fps que a 144 fps.
        var k = 1 - Math.Exp(-dt * 8);
        _shadow += (ShadowTarget(State) - _shadow) * k;
        _look += (LookGoal(center, r) - _look) * k;

        DrawHalo(context, center, r, t);

        // El satélite de Working pasa por detrás de la luna en la mitad de su órbita.
        var orbitAngle = t * 2.2;
        if (State == MoonState.Working && Math.Sin(orbitAngle) < 0)
            DrawSatellite(context, center, r, orbitAngle);

        if (Accessory == "headphones")
            DrawHeadphoneBand(context, center, r);

        DrawBody(context, center, r, t);
        DrawBlush(context, center, r);
        DrawEyes(context, center, r, t);
        DrawMouth(context, center, r, t);
        DrawAccessory(context, center, r, t);

        if (State == MoonState.Working && Math.Sin(orbitAngle) >= 0)
            DrawSatellite(context, center, r, orbitAngle);
        if (State == MoonState.Sleeping)
            DrawSleepingZs(context, center, r, t);
        if (State == MoonState.Thinking)
            DrawThinkingDots(context, center, r, t);
    }

    // Qué tanto tapa la sombra: 2 = luna llena, menos = más sombra (fase lunar).
    private static double ShadowTarget(MoonState state) => state switch
    {
        MoonState.Sleeping => 1.15,
        MoonState.Error => 1.45,
        _ => 1.75
    };

    private Vector LookGoal(Point center, double r)
    {
        switch (State)
        {
            case MoonState.Thinking: return new Vector(0.06, -0.07);
            case MoonState.Working: return new Vector(0, 0.05);
            case MoonState.Idle when LookTarget is { } target:
                Vector dir = (target - center) / (r * 4);
                return dir.Length > 0.07 ? dir / dir.Length * 0.07 : dir;
            default: return default;
        }
    }

    // Dos rebotes que pierden fuerza (amortiguamiento) durante 0.6 s.
    private double HopHeight(double t)
    {
        var x = (t - _reactStart) / 0.6;
        if (x is < 0 or > 1) return 0;
        return 0.18 * Math.Abs(Math.Sin(x * 2 * Math.PI)) * (1 - x);
    }

    private void DrawHalo(DrawingContext context, Point center, double r, double t)
    {
        var (c, alpha, speed) = State switch
        {
            MoonState.Thinking => (Color.Parse("#A99BFF"), 90, 3.0),
            MoonState.Working => (Skin.AccentColor, 95, 2.0),
            MoonState.Error => (Color.Parse("#FF6B6B"), 120, 6.0),
            MoonState.Sleeping => (Color.Parse("#8FB3FF"), 30, 1.0),
            _ => (Skin.HaloColor, 60, 1.5)
        };
        var a = (byte)(alpha * (0.75 + 0.25 * Math.Sin(t * speed)));

        var halo = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(a, c.R, c.G, c.B), 0.6),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1.0)
            }
        };
        context.DrawEllipse(halo, null, center, r * 1.45, r * 1.45);
    }

    private void DrawBody(DrawingContext context, Point center, double r, double t)
    {
        var body = new EllipseGeometry(new Rect(center.X - r, center.Y - r, 2 * r, 2 * r));
        context.DrawGeometry(Skin.Body, null, body);

        // Todo lo de este bloque queda recortado al círculo de la luna.
        using (context.PushGeometryClip(body))
        {
            DrawCrater(context, center, r, -0.45, -0.50, 0.16);
            DrawCrater(context, center, r, 0.50, -0.30, 0.11);
            DrawCrater(context, center, r, 0.35, 0.55, 0.18);

            context.DrawEllipse(Skin.Shadow, null, new Point(center.X + _shadow * r, center.Y), r, r);

            if (State == MoonState.Error)
            {
                var a = (byte)(50 + 30 * Math.Sin(t * 6));
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, 255, 90, 90)), null, center, r, r);
            }
        }
    }

    private void DrawBlush(DrawingContext context, Point center, double r)
    {
        if (Skin.Blush is null || State == MoonState.Error)
            return;
        for (var side = -1; side <= 1; side += 2)
            context.DrawEllipse(Skin.Blush, null, new Point(center.X + side * 0.45 * r, center.Y + 0.14 * r),
                0.1 * r, 0.06 * r);
    }

    private Point EyeCenter(Point center, double r, int side)
        => new(center.X + side * 0.30 * r + _look.X * r, center.Y - 0.05 * r + _look.Y * r);

    private void DrawEyes(DrawingContext context, Point center, double r, double t)
    {
        var pen = new Pen(Skin.Ink, 0.045 * r, lineCap: PenLineCap.Round);

        for (var side = -1; side <= 1; side += 2)
        {
            var p = EyeCenter(center, r, side);
            var s = 0.08 * r;

            switch (State)
            {
                case MoonState.Sleeping:
                    DrawCurve(context, pen, new Point(p.X - s, p.Y), new Point(p.X, p.Y + s), new Point(p.X + s, p.Y));
                    break;

                case MoonState.Error:
                    context.DrawLine(pen, new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y + s));
                    context.DrawLine(pen, new Point(p.X - s, p.Y + s), new Point(p.X + s, p.Y - s));
                    break;

                default:
                    var open = EyeOpenness(t) * (State == MoonState.Thinking ? 0.75 : 1.0);
                    var h = 0.09 * r * Math.Max(open, 0.08);
                    context.DrawEllipse(Skin.Ink, null, p, 0.09 * r, h);
                    if (open > 0.5) // brillo en el ojo
                        context.DrawEllipse(Skin.Sparkle, null, new Point(p.X + 0.03 * r, p.Y - 0.035 * r),
                            0.022 * r, 0.022 * r);
                    break;
            }
        }
    }

    private void DrawMouth(DrawingContext context, Point center, double r, double t)
    {
        var pen = new Pen(Skin.Ink, 0.04 * r, lineCap: PenLineCap.Round);
        var p = new Point(center.X, center.Y + 0.32 * r);
        var w = 0.12 * r;

        if (IsTalking)
        {
            var open = 0.25 + 0.75 * (0.5 + 0.5 * Math.Sin(t * 16));
            context.DrawEllipse(Skin.Ink, null, p, 0.13 * r, (0.015 + 0.09 * open) * r);
            return;
        }

        switch (State)
        {
            case MoonState.Sleeping:
                var o = 0.035 + 0.01 * Math.Sin(t * 2 * Math.PI / 6);
                context.DrawEllipse(Skin.Ink, null, p, o * r, o * r);
                break;
            case MoonState.Error: // boca triste
                DrawCurve(context, pen, new Point(p.X - w, p.Y + 0.04 * r),
                    new Point(p.X, p.Y - 0.06 * r), new Point(p.X + w, p.Y + 0.04 * r));
                break;
            case MoonState.Thinking: // "hmm" ladeado
                context.DrawLine(pen, new Point(p.X - 0.04 * r, p.Y), new Point(p.X + 0.08 * r, p.Y - 0.02 * r));
                break;
            case MoonState.Working:
                context.DrawLine(pen, new Point(p.X - 0.06 * r, p.Y), new Point(p.X + 0.06 * r, p.Y));
                break;
            default: // sonrisa
                DrawCurve(context, pen, new Point(p.X - w, p.Y - 0.02 * r),
                    new Point(p.X, p.Y + 0.08 * r), new Point(p.X + w, p.Y - 0.02 * r));
                break;
        }
    }

    // ---------- Accesorios ----------

    private void DrawAccessory(DrawingContext context, Point c, double r, double t)
    {
        switch (Accessory)
        {
            case "nightcap": DrawNightcap(context, c, r, t); break;
            case "headphones": DrawHeadphoneCups(context, c, r); break;
            case "crown": DrawCrown(context, c, r, t); break;
            case "glasses": DrawGlasses(context, c, r); break;
            case "helmet": DrawHelmet(context, c, r); break;
            case "bow": DrawBow(context, c, r); break;
        }
    }

    private void DrawNightcap(DrawingContext context, Point c, double r, double t)
    {
        // Gorro de dormir que se mece un poquito.
        var sway = 0.05 * r * Math.Sin(t * 1.3);
        var left = new Point(c.X - 0.62 * r, c.Y - 0.68 * r);
        var right = new Point(c.X + 0.52 * r, c.Y - 0.86 * r);
        var tip = new Point(c.X + 1.02 * r + sway, c.Y - 1.12 * r);

        var cap = new StreamGeometry();
        using (var g = cap.Open())
        {
            g.BeginFigure(left, true);
            g.QuadraticBezierTo(new Point(c.X - 0.2 * r, c.Y - 1.45 * r), tip);
            g.QuadraticBezierTo(new Point(c.X + 0.75 * r, c.Y - 1.0 * r), right);
            g.EndFigure(true);
        }
        context.DrawGeometry(Skin.Accent, null, cap);
        context.DrawLine(new Pen(White, 0.16 * r, lineCap: PenLineCap.Round), left, right);
        context.DrawEllipse(White, null, tip, 0.13 * r, 0.13 * r);
    }

    // La diadema va detrás de la cabeza; las almohadillas, delante.
    private void DrawHeadphoneBand(DrawingContext context, Point c, double r)
        => DrawCurve(context, new Pen(Skin.Ink, 0.09 * r, lineCap: PenLineCap.Round),
            new Point(c.X - 0.98 * r, c.Y), new Point(c.X, c.Y - 1.62 * r), new Point(c.X + 0.98 * r, c.Y));

    private void DrawHeadphoneCups(DrawingContext context, Point c, double r)
    {
        for (var side = -1; side <= 1; side += 2)
        {
            var p = new Point(c.X + side * 0.98 * r, c.Y + 0.05 * r);
            context.DrawEllipse(Skin.Accent, new Pen(Skin.Ink, 0.03 * r), p, 0.17 * r, 0.27 * r);
        }
    }

    private static void DrawCrown(DrawingContext context, Point c, double r, double t)
    {
        for (var i = -1; i <= 1; i++)
        {
            var bob = 0.04 * r * Math.Sin(t * 2.4 + i);
            var size = (i == 0 ? 0.19 : 0.14) * r;
            DrawStar(context, Gold, new Point(c.X + i * 0.42 * r, c.Y - (i == 0 ? 1.3 : 1.15) * r + bob), size);
        }
    }

    private void DrawGlasses(DrawingContext context, Point c, double r)
    {
        var pen = new Pen(Skin.Ink, 0.04 * r);
        var lens = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        var leftEye = EyeCenter(c, r, -1);
        var rightEye = EyeCenter(c, r, 1);
        context.DrawEllipse(lens, pen, leftEye, 0.18 * r, 0.16 * r);
        context.DrawEllipse(lens, pen, rightEye, 0.18 * r, 0.16 * r);
        context.DrawLine(pen, new Point(leftEye.X + 0.18 * r, leftEye.Y), new Point(rightEye.X - 0.18 * r, rightEye.Y));
    }

    private static void DrawHelmet(DrawingContext context, Point c, double r)
    {
        var glass = new SolidColorBrush(Color.FromArgb(28, 220, 235, 255));
        var rim = new Pen(new SolidColorBrush(Color.FromArgb(200, 220, 230, 255)), 0.06 * r);
        context.DrawEllipse(glass, rim, c, 1.25 * r, 1.25 * r);
        // reflejo
        DrawCurve(context, new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 0.07 * r, lineCap: PenLineCap.Round),
            new Point(c.X - 0.95 * r, c.Y - 0.35 * r), new Point(c.X - 0.85 * r, c.Y - 0.95 * r),
            new Point(c.X - 0.3 * r, c.Y - 1.1 * r));
        // antena
        context.DrawLine(rim, new Point(c.X + 0.55 * r, c.Y - 1.1 * r), new Point(c.X + 0.75 * r, c.Y - 1.45 * r));
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#FF7A7A")), null, new Point(c.X + 0.77 * r, c.Y - 1.5 * r),
            0.07 * r, 0.07 * r);
    }

    private void DrawBow(DrawingContext context, Point c, double r)
    {
        var knot = new Point(c.X - 0.5 * r, c.Y - 0.86 * r);
        foreach (var dir in new[] { -1, 1 })
        {
            var wing = new StreamGeometry();
            using (var g = wing.Open())
            {
                g.BeginFigure(knot, true);
                g.LineTo(new Point(knot.X + dir * 0.34 * r, knot.Y - 0.2 * r));
                g.LineTo(new Point(knot.X + dir * 0.34 * r, knot.Y + 0.2 * r));
                g.EndFigure(true);
            }
            context.DrawGeometry(Skin.Accent, new Pen(Skin.Ink, 0.025 * r, lineJoin: PenLineJoin.Round), wing);
        }
        context.DrawEllipse(Skin.Accent, new Pen(Skin.Ink, 0.025 * r), knot, 0.08 * r, 0.08 * r);
    }

    private static void DrawStar(DrawingContext context, IBrush brush, Point p, double size)
    {
        var star = new StreamGeometry();
        using (var g = star.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var radius = i % 2 == 0 ? size : size * 0.45;
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var point = new Point(p.X + radius * Math.Cos(angle), p.Y + radius * Math.Sin(angle));
                if (i == 0) g.BeginFigure(point, true);
                else g.LineTo(point);
            }
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, star);
    }

    // ---------- Efectos por estado ----------

    private void DrawSatellite(DrawingContext context, Point center, double r, double angle)
    {
        // Órbita inclinada: elipse ancha y baja.
        var p = new Point(center.X + Math.Cos(angle) * 1.35 * r, center.Y + Math.Sin(angle) * 0.35 * r);
        context.DrawEllipse(Skin.Accent, null, p, 0.07 * r, 0.07 * r);
    }

    private static void DrawSleepingZs(DrawingContext context, Point center, double r, double t)
    {
        // Tres "z" que suben y se desvanecen, cada una con un desfase de 1/3 de ciclo.
        for (var i = 0; i < 3; i++)
        {
            var phase = (t / 3 + i / 3.0) % 1.0;
            var size = 0.26 * r * (0.6 + 0.6 * phase);
            var text = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, FontWeight.Bold), size, ZBrush);
            var p = new Point(center.X + (0.65 + 0.35 * phase) * r, center.Y - (0.7 + 0.8 * phase) * r);

            using (context.PushOpacity(1 - phase))
                context.DrawText(text, p);
        }
    }

    private static void DrawThinkingDots(DrawingContext context, Point center, double r, double t)
    {
        for (var i = 0; i < 3; i++)
        {
            var a = 0.25 + 0.75 * Math.Max(0, Math.Sin(t * 4 - i * 0.8));
            var p = new Point(center.X + (0.55 + i * 0.2) * r, center.Y - (1.05 + i * 0.12) * r);
            using (context.PushOpacity(a))
                context.DrawEllipse(ZBrush, null, p, 0.06 * r, 0.06 * r);
        }
    }

    // Cada 4 segundos parpadea durante 0.2 s: 1 (abierto) -> 0 (cerrado) -> 1
    private static double EyeOpenness(double t)
    {
        var phase = t % 4.0;
        if (phase < 3.8) return 1.0;
        var x = (phase - 3.8) / 0.2;
        return Math.Abs(2 * x - 1);
    }

    private void DrawCrater(DrawingContext context, Point center, double r, double dx, double dy, double size)
    {
        var p = new Point(center.X + dx * r, center.Y + dy * r);
        context.DrawEllipse(Skin.Crater, null, p, size * r, size * r);
    }

    // Curva suave de "from" a "to" que se dobla hacia "control".
    private static void DrawCurve(DrawingContext context, IPen pen, Point from, Point control, Point to)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(from, false);
            g.QuadraticBezierTo(control, to);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}
