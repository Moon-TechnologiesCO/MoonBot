using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MoonBot.Core.Chat;

namespace MoonBot.Infrastructure.Ai;

// Cualquier servidor que hable el formato "chat/completions" de OpenAI:
// servicios en la nube que lo ofrecen, o servidores locales como LM Studio.
// La respuesta llega como Server-Sent Events: líneas "data: {...}" y al final "data: [DONE]".
public sealed class OpenAiCompatibleProvider(HttpClient http, Uri baseUrl, string model, string? apiKey)
    : IAiProvider
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
        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Content = JsonContent.Create(body);
        using var response = await HttpSafety.SendAsync(http, request, cancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]")
                yield break;

            using var json = JsonDocument.Parse(data);
            if (json.RootElement.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.GetString() is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await HttpSafety.SendAsync(http, request, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return json.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().Select(m => m.GetProperty("id").GetString()!).Order().ToList()
            : [];
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, HttpSafety.Combine(baseUrl, path));
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }
}
