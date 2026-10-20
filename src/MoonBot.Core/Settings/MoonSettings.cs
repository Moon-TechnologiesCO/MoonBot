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
}

public interface ISettingsStore
{
    MoonSettings Load();
    void Save(MoonSettings settings);
}
