using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoonBot.Core.Chat;

namespace MoonBot.Infrastructure.Ai;

// Modelos locales servidos por Ollama (https://ollama.com). Nada sale de este PC.
// La respuesta llega como NDJSON: una línea JSON por cada trozo.
public sealed class OllamaProvider(HttpClient http, Uri baseUrl, string model) : IAiProvider
{
    public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
        IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = true,
            ["messages"] = new JsonArray(conversation.Select(ToJson).ToArray<JsonNode?>())
        };
        if (tools.Count > 0)
            body["tools"] = HttpSafety.ToolsJson(tools);

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

            using var chunk = JsonDocument.Parse(line);
            var root = chunk.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new AiProviderException(AiErrorKind.Status, error.GetString() ?? "error");

            if (root.TryGetProperty("message", out var message))
            {
                // Los modelos que "piensan" mandan su razonamiento en message.thinking: no se muestra.
                if (message.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } text)
                    yield return new AiTextDelta(text);

                if (message.TryGetProperty("tool_calls", out var calls) && calls.GetArrayLength() > 0)
                    yield return new AiToolCalls(calls.EnumerateArray().Select(ParseCall).ToList());
            }

            if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                yield break;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HttpSafety.Combine(baseUrl, "api/tags"));
        using var response = await HttpSafety.SendAsync(http, request, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return json.RootElement.TryGetProperty("models", out var models)
            ? models.EnumerateArray().Select(m => m.GetProperty("name").GetString()!).Order().ToList()
            : [];
    }

    // Ollama manda los argumentos como objeto JSON; aquí se guardan como texto JSON.
    private static ToolCall ParseCall(JsonElement call, int index)
    {
        var function = call.GetProperty("function");
        var id = call.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var arguments = function.TryGetProperty("arguments", out var args)
            ? args.ValueKind == JsonValueKind.String ? args.GetString()! : args.GetRawText()
            : "{}";
        return new ToolCall(id ?? $"call_{index}", function.GetProperty("name").GetString()!, arguments);
    }

    private static JsonObject ToJson(ChatMessage message)
    {
        var json = new JsonObject
        {
            ["role"] = HttpSafety.RoleName(message.Role),
            ["content"] = message.Text
        };

        if (message.ToolCalls.Count > 0)
        {
            json["tool_calls"] = new JsonArray(message.ToolCalls.Select(c => (JsonNode?)new JsonObject
            {
                ["function"] = new JsonObject
                {
                    ["name"] = c.Name,
                    ["arguments"] = HttpSafety.ParseObjectOrEmpty(c.ArgumentsJson)
                }
            }).ToArray());
        }

        if (message.Role == ChatRole.Tool && message.ToolName is not null)
            json["tool_name"] = message.ToolName;

        return json;
    }
}
