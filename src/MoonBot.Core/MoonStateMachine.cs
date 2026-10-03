namespace MoonBot.Core;

public class MoonStateMachine
{
    public MoonState Current { get; } = MoonState.Idle;
}