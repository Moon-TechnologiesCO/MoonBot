namespace MoonBot.Core.Placement;

// Un rectángulo en píxeles físicos de pantalla.
public readonly record struct PixelBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public enum TaskbarEdge { Bottom, Top, Left, Right }

public enum PlacementMode
{
    Auto,       // junto al reloj, según el borde donde esté la barra de tareas
    TopCenter,  // arriba al centro, como un "notch"
    Custom      // donde el usuario la dejó al arrastrarla
}

// Matemáticas puras de colocación. No sabe nada de Windows: recibe rectángulos y devuelve
// coordenadas. Por eso se puede probar sin abrir ventanas.
public static class MoonPlacement
{
    // La barra de tareas es la franja que le falta al "área de trabajo" respecto a la pantalla.
    public static TaskbarEdge DetectTaskbar(PixelBox screen, PixelBox workArea)
    {
        var gaps = new (TaskbarEdge Edge, int Size)[]
        {
            (TaskbarEdge.Bottom, screen.Bottom - workArea.Bottom),
            (TaskbarEdge.Top, workArea.Y - screen.Y),
            (TaskbarEdge.Left, workArea.X - screen.X),
            (TaskbarEdge.Right, screen.Right - workArea.Right)
        };

        var biggest = gaps.MaxBy(g => g.Size);
        return biggest.Size > 0 ? biggest.Edge : TaskbarEdge.Bottom; // barra oculta: lo más común
    }

    public static (int X, int Y) Compute(PlacementMode mode, PixelBox screen, PixelBox workArea,
        int width, int height, int margin)
    {
        if (mode == PlacementMode.TopCenter)
            return (workArea.X + (workArea.Width - width) / 2, workArea.Y);

        // Auto: la esquina del reloj (área de notificación) es la más tranquila de la pantalla.
        return DetectTaskbar(screen, workArea) switch
        {
            TaskbarEdge.Top => (workArea.Right - width - margin, workArea.Y + margin),
            TaskbarEdge.Left => (workArea.X + margin, workArea.Bottom - height - margin),
            _ => (workArea.Right - width - margin, workArea.Bottom - height - margin)
        };
    }

    // Si la luna está pegada arriba, el globo de diálogo debe salir hacia abajo.
    public static bool BubbleBelow(PlacementMode mode, PixelBox screen, PixelBox workArea)
        => mode == PlacementMode.TopCenter || DetectTaskbar(screen, workArea) == TaskbarEdge.Top;

    public static (int X, int Y) Clamp(int x, int y, int width, int height, PixelBox workArea)
        => (Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width)),
            Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height)));

    // La ventana de chat se abre al lado de la luna, hacia el interior de la pantalla,
    // alineada con el borde inferior de la luna (o superior si la luna está arriba).
    public static (int X, int Y) PopupNextTo(PixelBox moon, int width, int height, PixelBox workArea, int gap)
    {
        var moonCenterX = moon.X + moon.Width / 2;
        var opensLeft = moonCenterX > workArea.X + workArea.Width / 2;
        var x = opensLeft ? moon.X - width - gap : moon.Right + gap;

        var moonIsHigh = moon.Y + moon.Height / 2 < workArea.Y + workArea.Height / 2;
        var y = moonIsHigh ? moon.Y : moon.Bottom - height;

        return Clamp(x, y, width, height, workArea);
    }
}
