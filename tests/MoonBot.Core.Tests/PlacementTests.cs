using MoonBot.Core.Placement;

namespace MoonBot.Core.Tests;

public class PlacementTests
{
    // Pantalla de 1920x1080 con la barra de tareas de 48 px en cada borde posible.
    private static readonly PixelBox Screen = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(0, 0, 1920, 1032, TaskbarEdge.Bottom)]
    [InlineData(0, 48, 1920, 1032, TaskbarEdge.Top)]
    [InlineData(48, 0, 1872, 1080, TaskbarEdge.Left)]
    [InlineData(0, 0, 1872, 1080, TaskbarEdge.Right)]
    [InlineData(0, 0, 1920, 1080, TaskbarEdge.Bottom)] // barra oculta: se asume abajo
    public void Taskbar_IsDetectedFromTheMissingStripOfWorkArea(int x, int y, int w, int h, TaskbarEdge expected)
    {
        Assert.Equal(expected, MoonPlacement.DetectTaskbar(Screen, new PixelBox(x, y, w, h)));
    }

    [Fact]
    public void Auto_BottomTaskbar_SitsAboveTheClockCorner()
    {
        var work = new PixelBox(0, 0, 1920, 1032);

        var (x, y) = MoonPlacement.Compute(PlacementMode.Auto, Screen, work, 300, 400, 12);

        Assert.Equal((1920 - 300 - 12, 1032 - 400 - 12), (x, y));
    }

    [Fact]
    public void Auto_TopTaskbar_HangsBelowItOnTheRight()
    {
        var work = new PixelBox(0, 48, 1920, 1032);

        var (x, y) = MoonPlacement.Compute(PlacementMode.Auto, Screen, work, 300, 400, 12);

        Assert.Equal((1920 - 300 - 12, 48 + 12), (x, y));
    }

    [Fact]
    public void Auto_LeftTaskbar_SitsNextToItAtTheBottom()
    {
        var work = new PixelBox(48, 0, 1872, 1080);

        var (x, y) = MoonPlacement.Compute(PlacementMode.Auto, Screen, work, 300, 400, 12);

        Assert.Equal((48 + 12, 1080 - 400 - 12), (x, y));
    }

    [Fact]
    public void Auto_RightTaskbar_SitsNextToItAtTheBottom()
    {
        var work = new PixelBox(0, 0, 1872, 1080);

        var (x, y) = MoonPlacement.Compute(PlacementMode.Auto, Screen, work, 300, 400, 12);

        Assert.Equal((1872 - 300 - 12, 1080 - 400 - 12), (x, y));
    }

    [Fact]
    public void TopCenter_SitsCenteredUnderTheTopEdge()
    {
        var work = new PixelBox(0, 0, 1920, 1032);

        var (x, y) = MoonPlacement.Compute(PlacementMode.TopCenter, Screen, work, 300, 400, 12);

        Assert.Equal(((1920 - 300) / 2, 0), (x, y));
    }

    [Theory]
    [InlineData(TaskbarEdge.Top, true)]
    [InlineData(TaskbarEdge.Bottom, false)]
    public void Bubble_GoesBelowTheMoon_OnlyWhenTheMoonIsAtTheTop(TaskbarEdge edge, bool below)
    {
        var work = edge == TaskbarEdge.Top ? new PixelBox(0, 48, 1920, 1032) : new PixelBox(0, 0, 1920, 1032);

        Assert.Equal(below, MoonPlacement.BubbleBelow(PlacementMode.Auto, Screen, work));
        Assert.True(MoonPlacement.BubbleBelow(PlacementMode.TopCenter, Screen, work));
    }

    [Fact]
    public void Clamp_KeepsAWindowFullyInsideTheWorkArea()
    {
        var work = new PixelBox(0, 0, 1920, 1032);

        Assert.Equal((1920 - 300, 0), MoonPlacement.Clamp(5000, -80, 300, 400, work));
    }

    [Fact]
    public void Popup_OpensToTheLeftOfAMoonInTheRightCorner_AlignedToItsBottom()
    {
        var work = new PixelBox(0, 0, 1920, 1032);
        var moon = new PixelBox(1608, 620, 300, 400);

        var (x, y) = MoonPlacement.PopupNextTo(moon, 420, 600, work, 8);

        Assert.Equal((1608 - 420 - 8, 1020 - 600), (x, y));
    }

    [Fact]
    public void Popup_OpensToTheRight_WhenTheMoonIsOnTheLeft()
    {
        var work = new PixelBox(48, 0, 1872, 1080);
        var moon = new PixelBox(60, 668, 300, 400);

        var (x, _) = MoonPlacement.PopupNextTo(moon, 420, 600, work, 8);

        Assert.Equal(360 + 8, x);
    }
}
