namespace MoonBot.Core.Chat;

public enum ChatRole { System, User, Assistant, Tool }

// Petición del modelo para ejecutar una herramienta. ArgumentsJson es lo que el modelo escribió:
// se trata como datos no confiables hasta que la herramienta lo valide.
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatMessage(ChatRole Role, string Text)
{
    // Solo en mensajes del asistente que piden herramientas.
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];

    // Solo en mensajes con Role = Tool: a qué petición responden.
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
}
