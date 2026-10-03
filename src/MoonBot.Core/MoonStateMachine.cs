namespace MoonBot.Core;

public class MoonStateMachine
{
    private static readonly Dictionary<MoonState, MoonState[]> Allowed = new()
    {
        [MoonState.Sleeping] = [MoonState.Idle, MoonState.Error],
        [MoonState.Idle]     = [MoonState.Sleeping, MoonState.Thinking, MoonState.Error],
        [MoonState.Thinking] = [MoonState.Working, MoonState.Idle, MoonState.Error],
        [MoonState.Working]  = [MoonState.Idle, MoonState.Error],
        [MoonState.Error]    = [MoonState.Error]
    };

    public MoonState Current { get; private set; } = MoonState.Idle;

    public bool TryChangeTo(MoonState target)
    {
        if (!Allowed[Current].Contains(target))
            return false;

        Current = target;
        return true;
    }
}