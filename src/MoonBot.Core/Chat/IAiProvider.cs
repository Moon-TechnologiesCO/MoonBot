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

// Error de un proveedor con un mensaje apto para mostrar al usuario (sin secretos).
public sealed class AiProviderException(string message, Exception? inner = null)
    : Exception(message, inner);
