using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace MoonBot.App;

// Una "piel" de la luna: solo colores. El dibujo (MoonView) es el mismo para todas.
// Añadir una skin nueva = añadir una línea al catálogo (abierto a extensión, cerrado a modificación).
public sealed class MoonSkin
{
    public MoonSkin(string id, string body, string crater, string ink, string halo, string shadow,
        double shadowAlpha, string accent, string? blush = null, string? sparkle = null)
    {
        Id = id;
        Body = new SolidColorBrush(Color.Parse(body));
        Crater = new SolidColorBrush(Color.Parse(crater));
        Ink = new SolidColorBrush(Color.Parse(ink));
        HaloColor = Color.Parse(halo);
        Shadow = new SolidColorBrush(Color.Parse(shadow), shadowAlpha);
        AccentColor = Color.Parse(accent);
        Accent = new SolidColorBrush(AccentColor);
        Blush = blush is null ? null : new SolidColorBrush(Color.Parse(blush), 0.55);
        Sparkle = new SolidColorBrush(Color.Parse(sparkle ?? body));
    }

    public string Id { get; }
    public string NameKey => $"skin.{Id}";
    public IBrush Body { get; }
    public IBrush Crater { get; }
    public IBrush Ink { get; }
    public Color HaloColor { get; }
    public IBrush Shadow { get; }
    public Color AccentColor { get; }
    public IBrush Accent { get; }
    public IBrush? Blush { get; }
    public IBrush Sparkle { get; }
}

public static class MoonSkins
{
    public static readonly IReadOnlyList<MoonSkin> All =
    [
        new("classic",  "#E8E4D4", "#CFCAB5", "#1B2140", "#FFE9A8", "#0B1026", 0.55, "#B6A4FF"),
        new("harvest",  "#F6D58E", "#E3B862", "#4A2A12", "#FFC76B", "#2A1A10", 0.50, "#FF9F43", blush: "#FF8F6B"),
        new("bluemoon", "#C7D8FF", "#A3BCF2", "#1A2350", "#8FB3FF", "#0B1026", 0.50, "#6F9BFF"),
        new("blood",    "#E98A6E", "#C7644C", "#3A0E0E", "#FF6B5B", "#2A0808", 0.55, "#FF5A4E"),
        new("sakura",   "#FFDCE8", "#F6BBD0", "#5A2240", "#FFB3CF", "#3A1030", 0.40, "#FF7FA8", blush: "#FF8FB1"),
        new("aurora",   "#C9F2E3", "#A6E3CC", "#13403A", "#7FE0C8", "#0B2622", 0.45, "#3FD0A8"),
        new("eclipse",  "#2E3150", "#25283F", "#F6E7B0", "#F5C46B", "#000000", 0.60, "#F5C46B", sparkle: "#2E3150")
    ];

    public static MoonSkin Get(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}

// Objetos que la luna puede llevar puestos.
public static class MoonAccessories
{
    public static readonly IReadOnlyList<string> All =
        ["none", "nightcap", "headphones", "crown", "glasses", "helmet", "bow"];

    public static string Get(string? id) => All.Contains(id) ? id! : "none";
}
