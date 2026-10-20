namespace MoonBot.Core.Chat;

// Cualquier servicio que sepa responder una conversación: Ollama, una nube, un falso para tests.
// ChatSession depende de esta interfaz, nunca de un proveedor concreto (inversión de dependencias).
public interface IAiProvider
{
    // Devuelve la respuesta en trozos a medida que se genera (streaming).
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
}

public enum AiErrorKind { Connection, Status, InsecureKey, Timeout }

// Error de un proveedor. Lleva el tipo (para que la UI lo traduzca) y un detalle técnico sin secretos.
public sealed class AiProviderException(AiErrorKind kind, string detail, Exception? inner = null)
    : Exception(detail, inner)
{
    public AiErrorKind Kind { get; } = kind;
    public string Detail { get; } = detail;
}
