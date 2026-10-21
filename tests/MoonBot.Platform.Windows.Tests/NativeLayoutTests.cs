using System.Runtime.Versioning;

namespace MoonBot.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public class NativeLayoutTests
{
    // Si la estructura INPUT no mide lo mismo que en winuser.h, SendInput falla siempre.
    [Fact]
    public void InputStruct_MatchesTheWindowsLayout()
    {
        Assert.Equal(Environment.Is64BitProcess ? 40 : 28, TextTyper.InputSize);
    }

    [Fact]
    public void KeyState_ReportsRightControlUpWhenNobodyPressesIt()
    {
        Assert.False(KeyState.IsDown(KeyState.RightControl));
    }
}
