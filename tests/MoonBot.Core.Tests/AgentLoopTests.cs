using MoonBot.Core.Chat;
using MoonBot.Core.Tools;

namespace MoonBot.Core.Tests;

public class AgentLoopTests
{
    private static readonly ToolCall SearchCall = new("c1", "search", """{"query":"budget"}""");

    private static ToolRouter Router(FakeTool tool, bool approve = true)
        => new([tool], new FakeApprovals(approve), new NullLog());

    [Fact]
    public async Task ModelAsksForATool_ToolRuns_ResultGoesBack_ThenItAnswers()
    {
        var tool = new FakeTool("search", ToolRisk.ReadOnly);
        var provider = new ScriptedAiProvider(
            [new AiToolCalls([SearchCall])],
            [new AiTextDelta("Found it.")]);
        var session = new ChatSession(provider, new MoonStateMachine(), Router(tool));

        var answer = await session.SendAsync("find budget", null, CancellationToken.None);

        Assert.Equal("Found it.", answer);
        Assert.Equal(1, tool.Runs);
        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant],
            session.History.Select(m => m.Role));
        Assert.Equal("c1", session.History[2].ToolCallId);
        // La segunda vuelta al modelo ya incluye el resultado de la herramienta.
        Assert.Contains(provider.Conversations[1], m => m.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task ToolDefinitions_AreOfferedToTheModel()
    {
        var provider = new ScriptedAiProvider([new AiTextDelta("hi")]);
        var session = new ChatSession(provider, new MoonStateMachine(), Router(new FakeTool("search", ToolRisk.ReadOnly)));

        await session.SendAsync("hi", null, CancellationToken.None);

        Assert.Equal("search", Assert.Single(provider.ToolsSeen[0]).Name);
    }

    [Fact]
    public async Task Observer_HearsToolStartAndFinish()
    {
        var provider = new ScriptedAiProvider([new AiToolCalls([SearchCall])], [new AiTextDelta("ok")]);
        var session = new ChatSession(provider, new MoonStateMachine(), Router(new FakeTool("search", ToolRisk.ReadOnly)));
        var events = new List<string>();

        await session.SendAsync("x", new ChatObserver
        {
            OnToolStarted = c => events.Add("start:" + c.Name),
            OnToolFinished = (c, o) => events.Add($"end:{c.Name}:{o.Ok}")
        }, CancellationToken.None);

        Assert.Equal(["start:search", "end:search:True"], events);
    }

    [Fact]
    public async Task ModelWithoutToolSupport_FallsBackToPlainChat_AndSaysSo()
    {
        var provider = new ScriptedAiProvider([new AiTextDelta("plain answer")]) { RejectTools = true };
        var session = new ChatSession(provider, new MoonStateMachine(), Router(new FakeTool("search", ToolRisk.ReadOnly)));
        var warned = 0;

        var answer = await session.SendAsync("x", new ChatObserver { OnToolsUnavailable = () => warned++ },
            CancellationToken.None);

        Assert.Equal("plain answer", answer);
        Assert.Equal(1, warned);
        Assert.Empty(provider.ToolsSeen[0]);
    }

    [Fact]
    public async Task EndlessToolRequests_StopAtTheStepLimit()
    {
        var turns = Enumerable.Repeat(new AiEvent[] { new AiToolCalls([SearchCall]) }, ChatSession.MaxToolSteps + 1)
            .ToArray();
        var tool = new FakeTool("search", ToolRisk.ReadOnly);
        var machine = new MoonStateMachine();
        var session = new ChatSession(new ScriptedAiProvider(turns), machine, Router(tool));

        await session.SendAsync("loop", null, CancellationToken.None);

        Assert.Equal(ChatSession.MaxToolSteps, tool.Runs);
        Assert.Equal(MoonState.Idle, machine.Current);
    }

    [Fact]
    public async Task MoonGoesThinkingWorkingThinkingIdle_DuringAToolRound()
    {
        var machine = new MoonStateMachine();
        var states = new List<MoonState>();
        machine.StateChanged += (_, e) => states.Add(e.To);
        var provider = new ScriptedAiProvider([new AiToolCalls([SearchCall])], [new AiTextDelta("ok")]);
        var session = new ChatSession(provider, machine, Router(new FakeTool("search", ToolRisk.ReadOnly)));

        await session.SendAsync("x", null, CancellationToken.None);

        Assert.Equal(
            [MoonState.Thinking, MoonState.Working, MoonState.Thinking, MoonState.Working, MoonState.Idle],
            states);
    }

    [Fact]
    public async Task CancelWhileAToolRuns_LeavesNoOrphanToolRequests()
    {
        var tool = new FakeTool("search", ToolRisk.ReadOnly) { Delay = TimeSpan.FromSeconds(10) };
        var provider = new ScriptedAiProvider([new AiToolCalls([SearchCall])], [new AiTextDelta("never")]);
        var session = new ChatSession(provider, new MoonStateMachine(), Router(tool));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.SendAsync("x", null, cts.Token));

        var requested = session.History.SelectMany(m => m.ToolCalls).Select(c => c.Id);
        var answered = session.History.Where(m => m.Role == ChatRole.Tool).Select(m => m.ToolCallId);
        Assert.Equal(requested, answered);
    }
}
