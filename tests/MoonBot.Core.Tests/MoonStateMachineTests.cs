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

    [Fact]
    public void TryChangeTo_FromIdleToThinking_ReturnsTrueAndChangesState()
    {
        // Arrange
        var machine = new MoonStateMachine();

        // Act
        var result = machine.TryChangeTo(MoonState.Thinking);

        // Assert
        Assert.True(result);
        Assert.Equal(MoonState.Thinking, machine.Current);
    }
}