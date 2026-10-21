using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoonBot.Core.Chat;

namespace MoonBot.Infrastructure.Ai;

// Cualquier servidor que hable el formato "chat/completions" de OpenAI:
// servicios en la nube que lo ofrecen, o servidores locales como LM Studio.
// La respuesta llega como Server-Sent Events: líneas "data: {...}" y al final "data: [DONE]".
public sealed class OpenAiCompatibleProvider(HttpClient http, Uri baseUrl, string model, string? apiKey)
    : IAiProvider
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

        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Content = JsonContent.Create(body);
        using var response = await HttpSafety.SendAsync(http, request, cancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));

        // Las peticiones de herramientas llegan en pedazos (por índice): se van juntando.
        var pending = new SortedDictionary<int, (string? Id, string? Name, StringBuilder Args)>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]")
                break;

            using var json = JsonDocument.Parse(data);
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;
            if (!choices[0].TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } text)
                yield return new AiTextDelta(text);

            if (delta.TryGetProperty("tool_calls", out var calls))
            {
                foreach (var call in calls.EnumerateArray())
                {
                    var index = call.TryGetProperty("index", out var i) ? i.GetInt32() : pending.Count;
                    var entry = pending.TryGetValue(index, out var existing) ? existing : (null, null, new StringBuilder());
                    if (call.TryGetProperty("id", out var id))
                        entry.Id = id.GetString();
                    if (call.TryGetProperty("function", out var function))
                    {
                        if (function.TryGetProperty("name", out var name))
                            entry.Name = name.GetString();
                        if (function.TryGetProperty("arguments", out var args))
                            entry.Args.Append(args.GetString());
                    }
                    pending[index] = entry;
                }
            }
        }

        if (pending.Count > 0)
        {
            yield return new AiToolCalls(pending
                .Where(p => p.Value.Name is not null)
                .Select(p => new ToolCall(p.Value.Id ?? $"call_{p.Key}", p.Value.Name!, p.Value.Args.ToString()))
                .ToList());
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
                ["id"] = c.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = c.Name, ["arguments"] = c.ArgumentsJson }
            }).ToArray());
        }

        if (message.Role == ChatRole.Tool)
            json["tool_call_id"] = message.ToolCallId;

        return json;
    }
}
