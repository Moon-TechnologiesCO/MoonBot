using MoonBot.Core.Voice;

namespace MoonBot.Core.Tests;

public class PushToTalkTests
{
    private static readonly TimeSpan MinHold = TimeSpan.FromMilliseconds(300);
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void PressingTheKey_StartsRecordingRightAway()
    {
        var ptt = new PushToTalk(MinHold);

        Assert.Equal(PushToTalkAction.StartRecording, ptt.Update(keyDown: true, otherKeyDown: false, Ms(0)));
        Assert.True(ptt.IsHeld);
    }

    [Fact]
    public void HoldingThenReleasing_Finishes()
    {
        var ptt = new PushToTalk(MinHold);
        ptt.Update(true, false, Ms(0));

        Assert.Equal(PushToTalkAction.None, ptt.Update(true, false, Ms(500)));
        Assert.Equal(PushToTalkAction.Finish, ptt.Update(false, false, Ms(1200)));
        Assert.False(ptt.IsHeld);
    }

    [Fact]
    public void AQuickTap_IsCancelled()
    {
        var ptt = new PushToTalk(MinHold);
        ptt.Update(true, false, Ms(0));

        Assert.Equal(PushToTalkAction.Cancel, ptt.Update(false, false, Ms(120)));
    }

    [Fact]
    public void UsingTheKeyInAShortcut_CancelsOnce_AndReleaseDoesNothing()
    {
        // Ctrl derecho + C = copiar: no es dictado.
        var ptt = new PushToTalk(MinHold);
        ptt.Update(true, false, Ms(0));

        Assert.Equal(PushToTalkAction.Cancel, ptt.Update(true, true, Ms(80)));
        Assert.Equal(PushToTalkAction.None, ptt.Update(true, true, Ms(200)));
        Assert.Equal(PushToTalkAction.None, ptt.Update(false, false, Ms(900)));
    }

    [Fact]
    public void PressingWhileAnotherKeyIsDown_DoesNotStart()
    {
        // Shift + Ctrl derecho (por ejemplo, para seleccionar): no es dictado.
        var ptt = new PushToTalk(MinHold);

        Assert.Equal(PushToTalkAction.None, ptt.Update(true, true, Ms(0)));
        Assert.Equal(PushToTalkAction.None, ptt.Update(false, false, Ms(800)));
    }

    [Fact]
    public void ItWorksAgainAfterAnyOutcome()
    {
        var ptt = new PushToTalk(MinHold);
        ptt.Update(true, false, Ms(0));
        ptt.Update(true, true, Ms(50));   // cancelado por atajo
        ptt.Update(false, false, Ms(100));

        Assert.Equal(PushToTalkAction.StartRecording, ptt.Update(true, false, Ms(2000)));
        Assert.Equal(PushToTalkAction.Finish, ptt.Update(false, false, Ms(3000)));
    }

    [Fact]
    public void HoldingTooLong_FinishesByItself()
    {
        // Evita grabar sin fin si la tecla se queda "pegada".
        var ptt = new PushToTalk(MinHold, maxHold: TimeSpan.FromSeconds(60));
        ptt.Update(true, false, Ms(0));

        Assert.Equal(PushToTalkAction.Finish, ptt.Update(true, false, TimeSpan.FromSeconds(61)));
        Assert.Equal(PushToTalkAction.None, ptt.Update(false, false, TimeSpan.FromSeconds(62)));
    }

    [Theory]
    [InlineData(" Hola, ¿qué tal?", "Hola, ¿qué tal?")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" (music) Buenos días [Música]", "Buenos días")]
    [InlineData("  línea uno\n línea dos ", "línea uno línea dos")]
    [InlineData("...", "")]
    public void Transcript_IsCleaned(string raw, string expected)
    {
        Assert.Equal(expected, Transcript.Clean(raw));
    }
}
