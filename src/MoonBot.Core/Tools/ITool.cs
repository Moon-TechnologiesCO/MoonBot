using System.Text.Json;
using MoonBot.Core.Chat;

namespace MoonBot.Core.Tools;

public enum ToolRisk
{
    ReadOnly, // solo mira (buscar, listar): se ejecuta sin preguntar
    Changes   // modifica algo (crear, renombrar, mover): SIEMPRE pide permiso
}

// Resumen legible de lo que la herramienta va a hacer, para la tarjeta de permiso.
// ActionKey es una clave de traducción (por ejemplo "tool.move").
public sealed record ToolPreview(string ActionKey, IReadOnlyList<string> Items, string? Target = null);

// Resultado: Json vuelve al modelo; Paths los muestra la interfaz como tarjetas con "Abrir".
public sealed record ToolOutcome(bool Ok, string Json, IReadOnlyList<string> Paths, bool Denied = false)
{
    public static ToolOutcome Fail(string message, bool denied = false)
        => new(false, JsonSerializer.Serialize(new { error = message }), [], denied);
}

public interface ITool
{
    ToolDefinition Definition { get; }
    ToolRisk Risk { get; }

    // Valida los argumentos y describe la acción. Lanza ToolArgumentException si algo no cuadra.
    ToolPreview Preview(JsonElement arguments);

    Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}

// Pregunta al usuario. La interfaz la implementa con una tarjeta "Permitir / Denegar".
public interface IApprovalGate
{
    Task<bool> RequestAsync(ToolCall call, ToolPreview preview, CancellationToken cancellationToken);
}

// Argumentos inválidos o fuera de lo permitido. El mensaje vuelve al modelo para que corrija.
public sealed class ToolArgumentException(string message) : Exception(message);
