using MoonBot.Core.Placement;

namespace MoonBot.Core.Settings;

public enum ProviderKind { Ollama, OpenAiCompatible }

// Preferencias del usuario. Se guardan en un archivo JSON, así que aquí NUNCA va un secreto:
// la API key vive en ISecretStore.
public sealed record MoonSettings
{
    public string Language { get; init; } = "en";
    public string UserName { get; init; } = "";
    public ProviderKind Provider { get; init; } = ProviderKind.Ollama;
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "gemma3:4b";
    public int SleepAfterMinutes { get; init; } = 2;

    // Apariencia de la luna.
    public string Skin { get; init; } = "classic";
    public string Accessory { get; init; } = "none";

    // Dónde vive la luna en la pantalla. CustomX/CustomY solo se usan con PlacementMode.Custom.
    public PlacementMode Placement { get; init; } = PlacementMode.Auto;
    public int? CustomX { get; init; }
    public int? CustomY { get; init; }

    // Herramientas de archivos (buscar, crear carpeta, renombrar, mover). Los cambios piden permiso siempre.
    public bool ToolsEnabled { get; init; } = true;
}

public interface ISettingsStore
{
    MoonSettings Load();
    void Save(MoonSettings settings);
}
