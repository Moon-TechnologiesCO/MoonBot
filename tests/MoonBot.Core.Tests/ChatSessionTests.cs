using System.Runtime.CompilerServices;
using MoonBot.Core.Chat;

namespace MoonBot.Core.Tests;

public class ChatSessionTests
{
    // Proveedor falso: responde con los trozos que le digamos y recuerda lo que recibió.
    private sealed class FakeAiProvider(params string[] chunks) : IAiProvider
    {
        public IReadOnlyList<ChatMessage>? LastConversation { get; private set; }

        public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
            IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastConversation = conversation.ToList();
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new AiTextDelta(chunk);
            }
        }

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(["fake"]);
    }

    private sealed class FailingAiProvider : IAiProvider
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
            IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new AiProviderException(AiErrorKind.Connection, "server is down");
#pragma warning disable CS0162 // un iterador necesita al menos un yield
            yield break;
#pragma warning restore CS0162
        }

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
            => throw new AiProviderException(AiErrorKind.Connection, "server is down");
    }

    [Fact]
    public async Task SendAsync_ReturnsJoinedReply_AndStoresBothMessages()
    {
        var session = new ChatSession(new FakeAiProvider("Hel", "lo!"), new MoonStateMachine());

        var reply = await session.SendAsync("Hi", null, CancellationToken.None);

        Assert.Equal("Hello!", reply);
        Assert.Equal(
            [new ChatMessage(ChatRole.User, "Hi"), new ChatMessage(ChatRole.Assistant, "Hello!")],
            session.History);
    }

    [Fact]
    public async Task SendAsync_ReportsEachChunkAsItArrives()
    {
        var session = new ChatSession(new FakeAiProvider("a", "b", "c"), new MoonStateMachine());
        var seen = new List<string>();

        await session.SendAsync("x", new ChatObserver { OnText = seen.Add }, CancellationToken.None);

        Assert.Equal(["a", "b", "c"], seen);
    }

    [Fact]
    public async Task SendAsync_MovesMoonThroughThinkingWorkingIdle()
    {
        var machine = new MoonStateMachine();
        var states = new List<MoonState>();
        machine.StateChanged += (_, e) => states.Add(e.To);
        var session = new ChatSession(new FakeAiProvider("ok"), machine);

        await session.SendAsync("x", null, CancellationToken.None);

        Assert.Equal([MoonState.Thinking, MoonState.Working, MoonState.Idle], states);
    }

    [Fact]
    public async Task SendAsync_PutsSystemPromptFirst()
    {
        var provider = new FakeAiProvider("ok");
        var session = new ChatSession(provider, new MoonStateMachine()) { SystemPrompt = "Be kind." };

        await session.SendAsync("x", null, CancellationToken.None);

        Assert.Equal(new ChatMessage(ChatRole.System, "Be kind."), provider.LastConversation![0]);
        Assert.Equal(new ChatMessage(ChatRole.User, "x"), provider.LastConversation[1]);
    }

    [Fact]
    public async Task SendAsync_WhenSleeping_WakesUpFirst()
    {
        var machine = new MoonStateMachine();
        machine.TryChangeTo(MoonState.Sleeping);
        var session = new ChatSession(new FakeAiProvider("ok"), machine);

        var reply = await session.SendAsync("x", null, CancellationToken.None);

        Assert.Equal("ok", reply);
        Assert.Equal(MoonState.Idle, machine.Current);
    }

    [Fact]
    public async Task SendAsync_ProviderFails_GoesToError_AndForgetsTheQuestion()
    {
        var machine = new MoonStateMachine();
        var session = new ChatSession(new FailingAiProvider(), machine);

        await Assert.ThrowsAsync<AiProviderException>(
            () => session.SendAsync("x", null, CancellationToken.None));

        Assert.Equal(MoonState.Error, machine.Current);
        Assert.Empty(session.History); // así el usuario puede reintentar sin una pregunta huérfana
        Assert.False(session.IsBusy);
    }

    [Fact]
    public async Task SendAsync_Cancelled_ReturnsToIdle()
    {
        var machine = new MoonStateMachine();
        var session = new ChatSession(new FakeAiProvider("a", "b"), machine);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.SendAsync("x", null, cts.Token));

        Assert.Equal(MoonState.Idle, machine.Current);
        Assert.False(session.IsBusy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_EmptyText_IsRejected(string text)
    {
        var session = new ChatSession(new FakeAiProvider("ok"), new MoonStateMachine());

        await Assert.ThrowsAsync<ArgumentException>(
            () => session.SendAsync(text, null, CancellationToken.None));
    }

    // Proveedor que no responde hasta que se abra la "puerta". Espera con await, sin bloquear hilos.
    private sealed class GatedAiProvider(Task gate) : IAiProvider
    {
        public async IAsyncEnumerable<AiEvent> StreamAsync(IReadOnlyList<ChatMessage> conversation,
            IReadOnlyList<ToolDefinition> tools, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await gate;
            yield return new AiTextDelta("done");
        }

        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    [Fact]
    public async Task SendAsync_WhileBusy_IsRejected()
    {
        var gate = new TaskCompletionSource();
        var session = new ChatSession(new GatedAiProvider(gate.Task), new MoonStateMachine());
        var first = session.SendAsync("one", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("two", null, CancellationToken.None));

        gate.SetResult();
        await first;
    }
}
