using System.Text.Json;
using MoonBot.Core.Chat;
using MoonBot.Core.Diagnostics;

namespace MoonBot.Core.Tools;

// El único camino entre el modelo y las herramientas. Reglas:
//   1. Falla cerrado: herramienta desconocida, desactivada o con argumentos inválidos -> rechazo.
//   2. Las herramientas que cambian algo piden permiso al usuario, siempre.
//   3. Tiempo límite por herramienta.
//   4. Todo queda en el registro de auditoría (solo local).
public sealed class ToolRouter(IReadOnlyList<ITool> tools, IApprovalGate approvals, IEventLog log)
{
    public static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    public bool Enabled { get; set; } = true;

    public IReadOnlyList<ToolDefinition> Definitions
        => Enabled ? tools.Select(t => t.Definition).ToList() : [];

    public ITool? Find(string name) => Enabled ? tools.FirstOrDefault(t => t.Definition.Name == name) : null;

    public async Task<ToolOutcome> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var tool = Find(call.Name);
        if (tool is null)
        {
            log.Info($"Tool denied: unknown or disabled '{call.Name}'");
            return ToolOutcome.Fail($"Tool '{call.Name}' is not available.", denied: true);
        }

        JsonDocument arguments;
        try
        {
            arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
        }
        catch (JsonException)
        {
            log.Info($"Tool rejected: {call.Name} with invalid JSON arguments");
            return ToolOutcome.Fail("The arguments are not valid JSON.");
        }

        using (arguments)
        {
            try
            {
                var preview = tool.Preview(arguments.RootElement);

                if (tool.Risk == ToolRisk.Changes)
                {
                    var allowed = await approvals.RequestAsync(call, preview, cancellationToken);
                    log.Info($"Tool {(allowed ? "approved" : "denied")} by user: {call.Name} {Describe(preview)}");
                    if (!allowed)
                        return ToolOutcome.Fail("The user denied this action. Do not retry it.", denied: true);
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ToolTimeout);
                var outcome = await tool.ExecuteAsync(arguments.RootElement, timeout.Token);
                log.Info($"Tool ran: {call.Name} ok={outcome.Ok}");
                return outcome;
            }
            catch (ToolArgumentException e)
            {
                log.Info($"Tool rejected: {call.Name} ({e.Message})");
                return ToolOutcome.Fail(e.Message);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log.Error($"Tool timed out: {call.Name}");
                return ToolOutcome.Fail("The tool took too long and was stopped.");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Error($"Tool failed: {call.Name}", e);
                return ToolOutcome.Fail(e.Message);
            }
        }
    }

    // Para la auditoría: qué acción y sobre qué rutas (el log vive solo en este PC).
    private static string Describe(ToolPreview preview)
        => $"{preview.ActionKey} [{string.Join("; ", preview.Items)}]{(preview.Target is null ? "" : " -> " + preview.Target)}";
}
