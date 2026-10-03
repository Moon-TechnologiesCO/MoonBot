using MoonBot.Core;

namespace MoonBot.Core.Tests;

public class MoonStateMachineTests
{
    [Fact]
    public void NewMachine_StartsInIdleState()
    {
        var machine = new MoonStateMachine();

        Assert.Equal(MoonState.Idle, machine.Current);
    }
}