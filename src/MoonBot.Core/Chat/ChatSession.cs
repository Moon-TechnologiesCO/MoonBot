using System.Text;
using MoonBot.Core.Tools;

namespace MoonBot.Core.Chat;

// Quien quiera enterarse de lo que pasa durante una respuesta (la interfaz, un test).
public sealed class ChatObserver
{
    public Action<string>? OnText { get; init; }
    public Action<ToolCall>? OnToolStarted { get; init; }
    public Action<ToolCall, ToolOutcome>? OnToolFinished { get; init; }
    public Action? OnToolsUnavailable { get; init; }
}

// Coordina una conversación: guarda el historial, llama al proveedor, ejecuta las herramientas
// que el modelo pida (a través del ToolRouter) y mueve a la luna por sus estados.
public sealed class ChatSession(IAiProvider provider, MoonStateMachine machine, ToolRouter? tools = null)
{
    // Límite de vueltas modelo -> herramienta -> modelo por cada mensaje del usuario.
    public const int MaxToolSteps = 6;

    private readonly List<ChatMessage> _history = [];
    private IAiProvider _provider = provider;
    private bool _providerLacksTools;

    public string SystemPrompt { get; set; } = "";
    public IReadOnlyList<ChatMessage> History => _history;
    public bool IsBusy { get; private set; }

    public void UseProvider(IAiProvider newProvider)
    {
        _provider = newProvider;
        _providerLacksTools = false;
    }

    public void Clear() => _history.Clear();

    public async Task<string> SendAsync(string text, ChatObserver? observer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("The message is empty.", nameof(text));
        if (IsBusy)
            throw new InvalidOperationException("A reply is already in progress.");

        IsBusy = true;
        var turnStart = _history.Count;
        _history.Add(new ChatMessage(ChatRole.User, text.Trim()));

        // Desde Sleeping o Error no se puede pensar directamente: primero se despierta.
        if (machine.Current is MoonState.Sleeping or MoonState.Error)
            machine.TryChangeTo(MoonState.Idle);
        machine.TryChangeTo(MoonState.Thinking);

        var reply = new StringBuilder();
        try
        {
            for (var step = 0; ; step++)
            {
                reply.Clear();
                var calls = await StreamStepAsync(reply, observer, cancellationToken);

                if (calls.Count == 0 || step >= MaxToolSteps)
                {
                    var answer = reply.ToString().Trim();
                    _history.Add(new ChatMessage(ChatRole.Assistant, answer));
                    machine.TryChangeTo(MoonState.Idle);
                    return answer;
                }

                // El modelo pidió herramientas: se ejecutan una a una y su resultado vuelve al modelo.
                _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()) { ToolCalls = calls });
                reply.Clear(); // ya quedó guardado: si cancelan ahora, no se duplica
                machine.TryChangeTo(MoonState.Working);
                foreach (var call in calls)
                {
                    observer?.OnToolStarted?.Invoke(call);
                    var outcome = await tools!.ExecuteAsync(call, cancellationToken);
                    _history.Add(ToolResult(call, outcome.Json));
                    observer?.OnToolFinished?.Invoke(call, outcome);
                }
                machine.TryChangeTo(MoonState.Thinking);
            }
        }
        catch (OperationCanceledException)
        {
            // Si el usuario detuvo la respuesta, se guarda lo que alcanzó a llegar.
            if (reply.Length > 0)
                _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString().Trim()));
            CloseTurn(turnStart);
            machine.TryChangeTo(MoonState.Idle);
            throw;
        }
        catch
        {
            CloseTurn(turnStart);
            machine.TryChangeTo(MoonState.Error);
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Una vuelta con el modelo. Devuelve las herramientas pedidas (vacío si respondió con texto).
    private async Task<List<ToolCall>> StreamStepAsync(StringBuilder reply, ChatObserver? observer,
        CancellationToken cancellationToken)
    {
        var useTools = tools is not null && !_providerLacksTools;
        try
        {
            return await StreamOnceAsync(useTools ? tools!.Definitions : [], reply, observer, cancellationToken);
        }
        catch (AiProviderException e) when (e.Kind == AiErrorKind.ToolsNotSupported && useTools)
        {
            // Este modelo no sabe usar herramientas: se avisa una vez y se sigue como chat normal.
            _providerLacksTools = true;
            observer?.OnToolsUnavailable?.Invoke();
            return await StreamOnceAsync([], reply, observer, cancellationToken);
        }
    }

    private async Task<List<ToolCall>> StreamOnceAsync(IReadOnlyList<ToolDefinition> definitions,
        StringBuilder reply, ChatObserver? observer, CancellationToken cancellationToken)
    {
        var calls = new List<ToolCall>();
        await foreach (var item in _provider.StreamAsync(BuildConversation(), definitions, cancellationToken))
        {
            switch (item)
            {
                case AiTextDelta { Text.Length: > 0 } delta:
                    if (reply.Length == 0)
                        machine.TryChangeTo(MoonState.Working);
                    reply.Append(delta.Text);
                    observer?.OnText?.Invoke(delta.Text);
                    break;
                case AiToolCalls requested:
                    calls.AddRange(requested.Calls);
                    break;
            }
        }
        return calls;
    }

    // Deja el historial válido tras un error o una cancelación:
    //  - si solo quedó la pregunta, se borra (el usuario puede reintentar);
    //  - si quedaron herramientas pedidas sin respuesta, se responden como canceladas,
    //    porque los proveedores rechazan una conversación con peticiones huérfanas.
    private void CloseTurn(int turnStart)
    {
        if (_history.Count == turnStart + 1)
        {
            _history.RemoveAt(turnStart);
            return;
        }

        var answered = _history.Skip(turnStart).Where(m => m.Role == ChatRole.Tool)
            .Select(m => m.ToolCallId).ToHashSet();
        var pending = _history.Skip(turnStart).SelectMany(m => m.ToolCalls)
            .Where(c => !answered.Contains(c.Id)).ToList();
        foreach (var call in pending)
            _history.Add(ToolResult(call, """{"error":"Cancelled before it ran."}"""));
    }

    private static ChatMessage ToolResult(ToolCall call, string json)
        => new(ChatRole.Tool, json) { ToolCallId = call.Id, ToolName = call.Name };

    private List<ChatMessage> BuildConversation()
    {
        var conversation = new List<ChatMessage>(_history.Count + 1);
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
            conversation.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        conversation.AddRange(_history);
        return conversation;
    }
}
