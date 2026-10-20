using System.Text;

namespace MoonBot.Core.Chat;

// Coordina una conversación: guarda el historial, llama al proveedor
// y mueve a la luna por sus estados (Thinking -> Working -> Idle, o Error).
public sealed class ChatSession(IAiProvider provider, MoonStateMachine machine)
{
    private readonly List<ChatMessage> _history = [];
    private IAiProvider _provider = provider;

    public string SystemPrompt { get; set; } = "";
    public IReadOnlyList<ChatMessage> History => _history;
    public bool IsBusy { get; private set; }

    public void UseProvider(IAiProvider newProvider) => _provider = newProvider;

    public void Clear() => _history.Clear();

    public async Task<string> SendAsync(string text, Action<string>? onChunk,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("The message is empty.", nameof(text));
        if (IsBusy)
            throw new InvalidOperationException("A reply is already in progress.");

        IsBusy = true;
        var question = new ChatMessage(ChatRole.User, text.Trim());
        _history.Add(question);

        // Desde Sleeping o Error no se puede pensar directamente: primero se despierta.
        if (machine.Current is MoonState.Sleeping or MoonState.Error)
            machine.TryChangeTo(MoonState.Idle);
        machine.TryChangeTo(MoonState.Thinking);

        var reply = new StringBuilder();
        try
        {
            await foreach (var chunk in _provider.StreamReplyAsync(BuildConversation(), cancellationToken))
            {
                if (reply.Length == 0)
                    machine.TryChangeTo(MoonState.Working);

                reply.Append(chunk);
                onChunk?.Invoke(chunk);
            }

            var answer = reply.ToString().Trim();
            _history.Add(new ChatMessage(ChatRole.Assistant, answer));
            machine.TryChangeTo(MoonState.Idle);
            return answer;
        }
        catch (OperationCanceledException)
        {
            // Si el usuario detuvo la respuesta, se guarda lo que alcanzó a llegar.
            if (reply.Length > 0)
                _history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString().Trim()));
            else
                _history.Remove(question);
            machine.TryChangeTo(MoonState.Idle);
            throw;
        }
        catch
        {
            _history.Remove(question);
            machine.TryChangeTo(MoonState.Error);
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<ChatMessage> BuildConversation()
    {
        var conversation = new List<ChatMessage>(_history.Count + 1);
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
            conversation.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        conversation.AddRange(_history);
        return conversation;
    }
}
