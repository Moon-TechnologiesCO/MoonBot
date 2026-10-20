using System.Runtime.CompilerServices;
using System.Text.Json;
using MoonBot.Core.Chat;
using MoonBot.Core.Diagnostics;
using MoonBot.Core.Tools;

namespace MoonBot.Core.Tests;

// Proveedor con guion: cada llamada reproduce la siguiente "vuelta" de eventos.
internal sealed class ScriptedAiProvider(params AiEvent[][] turns) : IAiProvider
{
    private int _turn;
    public List<IReadOnlyList<ChatMessage>> Conversations { get; } = [];
    public List<IReadOnlyList<ToolDefinition>> ToolsSeen { get; } = [];
    public bool RejectTools { get; init; }

    public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
        IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (RejectTools && tools.Count > 0)
            throw new AiProviderException(AiErrorKind.ToolsNotSupported, "model does not support tools");

        Conversations.Add(conversation.ToList());
        ToolsSeen.Add(tools);
        foreach (var item in turns[_turn++])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

// Herramienta falsa que registra si se ejecutó.
internal sealed class FakeTool(string name, ToolRisk risk, Func<JsonElement, ToolOutcome>? run = null) : ITool
{
    public int Runs { get; private set; }
    public TimeSpan Delay { get; init; }

    public ToolDefinition Definition { get; } = new(name, "test tool", """{"type":"object"}""");
    public ToolRisk Risk { get; } = risk;

    public ToolPreview Preview(JsonElement arguments)
    {
        if (arguments.TryGetProperty("bad", out _))
            throw new ToolArgumentException("bad argument");
        return new ToolPreview("tool.test", ["item"]);
    }

    public async Task<ToolOutcome> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        Runs++;
        await Task.Delay(Delay, cancellationToken);
        return run?.Invoke(arguments) ?? new ToolOutcome(true, """{"ok":true}""", ["C:\\file.txt"]);
    }
}

internal sealed class FakeApprovals(bool answer) : IApprovalGate
{
    public int Asked { get; private set; }

    public Task<bool> RequestAsync(ToolCall call, ToolPreview preview, CancellationToken cancellationToken)
    {
        Asked++;
        return Task.FromResult(answer);
    }
}

internal sealed class NullLog : IEventLog
{
    public List<string> Lines { get; } = [];
    public void Info(string message) => Lines.Add(message);
    public void Error(string message, Exception? exception = null) => Lines.Add(message);
}
