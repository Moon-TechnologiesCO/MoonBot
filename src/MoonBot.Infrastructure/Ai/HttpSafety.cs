using System.Text.Json;
using MoonBot.Core.Chat;

namespace MoonBot.Infrastructure.Ai;

// Reglas y utilidades comunes de HTTP para todos los proveedores.
internal static class HttpSafety
{
    // Una API key nunca viaja sin cifrar por la red: solo HTTPS, o HTTP dentro de este PC.
    public static void EnsureKeyCanBeSent(Uri baseUrl)
    {
        if (baseUrl.Scheme != Uri.UriSchemeHttps && !baseUrl.IsLoopback)
            throw new AiProviderException(AiErrorKind.InsecureKey, baseUrl.GetLeftPart(UriPartial.Authority));
    }

    // new Uri("https://x/v1", "chat") da "https://x/chat": sin la barra final se pierde "v1".
    public static Uri Combine(Uri baseUrl, string path)
    {
        var text = baseUrl.ToString();
        return new Uri(new Uri(text.EndsWith('/') ? text : text + "/"), path);
    }

    // Envía la petición y convierte los fallos de red y de servidor en AiProviderException.
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new AiProviderException(AiErrorKind.Connection,
                request.RequestUri!.GetLeftPart(UriPartial.Authority), e);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelado sin que el usuario lo pidiera: fue el Timeout de HttpClient.
            throw new AiProviderException(AiErrorKind.Timeout, request.RequestUri!.Host, e);
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AiProviderException(AiErrorKind.Status,
                $"{(int)response.StatusCode} {ExtractErrorMessage(body)}");
        }
    }

    // Ollama responde {"error":"..."}; las APIs tipo OpenAI, {"error":{"message":"..."}}.
    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString()!;
                if (error.TryGetProperty("message", out var message))
                    return message.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 200 ? body[..200] : body;
    }

    public static string RoleName(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.Assistant => "assistant",
        _ => "user"
    };
}
