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

    [Fact]
    public void TryChangeTo_FromSleepingToWorking_ReturnsFalseAndKeepsState()
    {
        // Arrange: llevo la máquina a Sleeping (Idle -> Sleeping es legal)
        var machine = new MoonStateMachine();
        machine.TryChangeTo(MoonState.Sleeping);

        // Act: intento un cambio ilegal
        var result = machine.TryChangeTo(MoonState.Working);

        // Assert: se rechaza y el estado no cambia
        Assert.False(result);
        Assert.Equal(MoonState.Sleeping, machine.Current);
    }

    [Fact]
    public void TryChangeTo_FromErrorToIdle_ReturnsTrue()
    {
        // Arrange: Idle -> Error es legal
        var machine = new MoonStateMachine();
        machine.TryChangeTo(MoonState.Error);

        // Act: la única salida de Error es volver a Idle
        var result = machine.TryChangeTo(MoonState.Idle);

        // Assert
        Assert.True(result);
        Assert.Equal(MoonState.Idle, machine.Current);
    }

    // Camino legal desde Idle hasta cada estado. Si agregas un estado al enum
    // y no lo agregas aquí, el test de abajo falla y te obliga a pensar en él.
    private static readonly Dictionary<MoonState, MoonState[]> PathFromIdle = new()
    {
        [MoonState.Idle]     = [],
        [MoonState.Sleeping] = [MoonState.Sleeping],
        [MoonState.Thinking] = [MoonState.Thinking],
        [MoonState.Working]  = [MoonState.Thinking, MoonState.Working],
        [MoonState.Error]    = [MoonState.Error]
    };

    [Fact]
    public void EveryState_HasAtLeastOneLegalExit()
    {
        foreach (var state in Enum.GetValues<MoonState>())
        {
            var hasExit = Enum.GetValues<MoonState>().Any(target =>
            {
                var machine = new MoonStateMachine();
                foreach (var step in PathFromIdle[state])
                    machine.TryChangeTo(step);

                return target != state && machine.TryChangeTo(target);
            });

            Assert.True(hasExit, $"{state} es un callejón sin salida");
        }
    }

    [Fact]
    public void TryChangeTo_LegalChange_RaisesStateChangedOnce()
    {
        // Arrange
        var machine = new MoonStateMachine();
        var raised = new List<(MoonState From, MoonState To)>();
        machine.StateChanged += (_, args) => raised.Add((args.From, args.To));

        // Act
        machine.TryChangeTo(MoonState.Thinking);

        // Assert
        Assert.Single(raised);
        Assert.Equal((MoonState.Idle, MoonState.Thinking), raised[0]);
    }

    [Fact]
    public void StateChanged_ListenerSeesNewCurrentState()
    {
        // Arrange
        var machine = new MoonStateMachine();
        MoonState? seen = null;
        machine.StateChanged += (_, _) => seen = machine.Current;

        // Act
        machine.TryChangeTo(MoonState.Thinking);

        // Assert: el aviso llega cuando el cambio ya está hecho
        Assert.Equal(MoonState.Thinking, seen);
    }

    [Fact]
    public void TryChangeTo_IllegalChange_DoesNotRaiseStateChanged()
    {
        // Arrange
        var machine = new MoonStateMachine();
        machine.TryChangeTo(MoonState.Sleeping);
        var count = 0;
        machine.StateChanged += (_, _) => count++;

        // Act
        machine.TryChangeTo(MoonState.Working);

        // Assert
        Assert.Equal(0, count);
    }
}