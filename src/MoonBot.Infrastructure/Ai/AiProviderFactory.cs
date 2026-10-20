using MoonBot.Core.Chat;
using MoonBot.Core.Settings;

namespace MoonBot.Infrastructure.Ai;

// Patrón Factory: un solo lugar decide qué proveedor concreto crear a partir de los ajustes.
public static class AiProviderFactory
{
    public static readonly IReadOnlyDictionary<ProviderKind, string> DefaultUrls =
        new Dictionary<ProviderKind, string>
        {
            [ProviderKind.Ollama] = "http://localhost:11434",
            [ProviderKind.OpenAiCompatible] = "http://localhost:1234/v1"
        };

    public static IAiProvider Create(MoonSettings settings, string? apiKey, HttpClient http)
    {
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUrl)
            || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The server URL is not a valid http(s) address.", nameof(settings));
        }

        if (!string.IsNullOrEmpty(apiKey))
            HttpSafety.EnsureKeyCanBeSent(baseUrl);

        return settings.Provider switch
        {
            ProviderKind.OpenAiCompatible => new OpenAiCompatibleProvider(http, baseUrl, settings.Model, apiKey),
            _ => new OllamaProvider(http, baseUrl, settings.Model)
        };
    }
}
