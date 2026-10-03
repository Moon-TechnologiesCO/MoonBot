namespace MoonBot.Core;

public class MoonStateMachine
{
    public MoonState Current { get; private set; } = MoonState.Idle;

    public bool TryChangeTo(MoonState target)
    {
        Current = target;
        return true;
    }
}