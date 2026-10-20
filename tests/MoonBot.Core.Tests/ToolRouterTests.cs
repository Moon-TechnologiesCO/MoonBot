using MoonBot.Core.Chat;
using MoonBot.Core.Tools;

namespace MoonBot.Core.Tests;

public class ToolRouterTests
{
    private static ToolCall Call(string name, string args = "{}") => new("1", name, args);

    [Fact]
    public async Task UnknownTool_IsDenied()
    {
        var router = new ToolRouter([new FakeTool("search", ToolRisk.ReadOnly)], new FakeApprovals(true), new NullLog());

        var outcome = await router.ExecuteAsync(Call("delete_everything"), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.True(outcome.Denied);
    }

    [Fact]
    public async Task ReadOnlyTool_RunsWithoutAsking()
    {
        var tool = new FakeTool("search", ToolRisk.ReadOnly);
        var approvals = new FakeApprovals(false);
        var router = new ToolRouter([tool], approvals, new NullLog());

        var outcome = await router.ExecuteAsync(Call("search"), CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(1, tool.Runs);
        Assert.Equal(0, approvals.Asked);
    }

    [Fact]
    public async Task ChangingTool_DeniedByUser_NeverRuns()
    {
        var tool = new FakeTool("move", ToolRisk.Changes);
        var approvals = new FakeApprovals(false);
        var router = new ToolRouter([tool], approvals, new NullLog());

        var outcome = await router.ExecuteAsync(Call("move"), CancellationToken.None);

        Assert.Equal(1, approvals.Asked);
        Assert.Equal(0, tool.Runs);
        Assert.True(outcome.Denied);
        Assert.Contains("denied", outcome.Json);
    }

    [Fact]
    public async Task ChangingTool_ApprovedByUser_Runs()
    {
        var tool = new FakeTool("move", ToolRisk.Changes);
        var router = new ToolRouter([tool], new FakeApprovals(true), new NullLog());

        var outcome = await router.ExecuteAsync(Call("move"), CancellationToken.None);

        Assert.True(outcome.Ok);
        Assert.Equal(1, tool.Runs);
    }

    [Fact]
    public async Task InvalidJson_IsRejectedBeforeAskingOrRunning()
    {
        var tool = new FakeTool("move", ToolRisk.Changes);
        var approvals = new FakeApprovals(true);
        var router = new ToolRouter([tool], approvals, new NullLog());

        var outcome = await router.ExecuteAsync(Call("move", "{not json"), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal(0, approvals.Asked);
        Assert.Equal(0, tool.Runs);
    }

    [Fact]
    public async Task InvalidArguments_AreRejectedBeforeAsking()
    {
        var tool = new FakeTool("move", ToolRisk.Changes);
        var approvals = new FakeApprovals(true);
        var router = new ToolRouter([tool], approvals, new NullLog());

        var outcome = await router.ExecuteAsync(Call("move", """{"bad":1}"""), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("bad argument", outcome.Json);
        Assert.Equal(0, approvals.Asked);
    }

    [Fact]
    public async Task DisabledRouter_OffersNoToolsAndRunsNothing()
    {
        var tool = new FakeTool("search", ToolRisk.ReadOnly);
        var router = new ToolRouter([tool], new FakeApprovals(true), new NullLog()) { Enabled = false };

        var outcome = await router.ExecuteAsync(Call("search"), CancellationToken.None);

        Assert.Empty(router.Definitions);
        Assert.True(outcome.Denied);
        Assert.Equal(0, tool.Runs);
    }

    [Fact]
    public async Task EveryDecision_IsWrittenToTheAuditLog()
    {
        var log = new NullLog();
        var router = new ToolRouter([new FakeTool("move", ToolRisk.Changes)], new FakeApprovals(false), log);

        await router.ExecuteAsync(Call("move"), CancellationToken.None);

        Assert.Contains(log.Lines, l => l.Contains("denied by user") && l.Contains("move"));
    }
}
