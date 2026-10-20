namespace MoonBot.Core.Chat;

// Lo que el modelo va produciendo mientras responde.
public abstract record AiEvent;
public sealed record AiTextDelta(string Text) : AiEvent;
public sealed record AiToolCalls(IReadOnlyList<ToolCall> Calls) : AiEvent;

// Descripción de una herramienta tal como se le presenta al modelo (JSON Schema de sus parámetros).
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

// Cualquier servicio que sepa responder una conversación: Ollama, una nube, un falso para tests.
// ChatSession depende de esta interfaz, nunca de un proveedor concreto (inversión de dependencias).
public interface IAiProvider
{
    // Devuelve la respuesta en trozos a medida que se genera (streaming).
    // Si "tools" no está vacío, el modelo puede pedir herramientas en lugar de responder.
    IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
}

public enum AiErrorKind { Connection, Status, InsecureKey, Timeout, ToolsNotSupported }

// Error de un proveedor. Lleva el tipo (para que la UI lo traduzca) y un detalle técnico sin secretos.
public sealed class AiProviderException(AiErrorKind kind, string detail, Exception? inner = null)
    : Exception(detail, inner)
{
    public AiErrorKind Kind { get; } = kind;
    public string Detail { get; } = detail;
}
