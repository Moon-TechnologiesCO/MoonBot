using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MoonBot.Core.Chat;

namespace MoonBot.Infrastructure.Ai;

// Modelos locales servidos por Ollama (https://ollama.com). Nada sale de este PC.
// La respuesta llega como NDJSON: una línea JSON por cada trozo de texto.
public sealed class OllamaProvider(HttpClient http, Uri baseUrl, string model) : IAiProvider
{
    public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new
        {
            model,
            stream = true,
            messages = conversation.Select(m => new { role = HttpSafety.RoleName(m.Role), content = m.Text })
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, HttpSafety.Combine(baseUrl, "api/chat"))
        {
            Content = JsonContent.Create(body)
        };
        using var response = await HttpSafety.SendAsync(http, request, cancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var chunk = JsonSerializer.Deserialize<Chunk>(line);
            if (chunk?.Error is { } error)
                throw new AiProviderException(AiErrorKind.Status, error);

            // Los modelos que "piensan" mandan su razonamiento en message.thinking: no se muestra.
            if (chunk?.Message?.Content is { Length: > 0 } text)
                yield return text;

            if (chunk?.Done == true)
                yield break;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HttpSafety.Combine(baseUrl, "api/tags"));
        using var response = await HttpSafety.SendAsync(http, request, cancellationToken);
        var tags = await response.Content.ReadFromJsonAsync<Tags>(cancellationToken);

        return tags?.Models?.Select(m => m.Name).Order().ToList() ?? [];
    }

    private sealed record Chunk(
        [property: JsonPropertyName("message")] Message? Message,
        [property: JsonPropertyName("done")] bool Done,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record Message([property: JsonPropertyName("content")] string? Content);

    private sealed record Tags([property: JsonPropertyName("models")] List<Tag>? Models);

    private sealed record Tag([property: JsonPropertyName("name")] string Name);
}
