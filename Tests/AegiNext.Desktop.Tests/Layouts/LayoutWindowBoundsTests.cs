using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

public sealed class LayoutWindowBoundsTests
{
    [Fact]
    public void RemovedDisplayMovesFloatingWindowsInsideCurrentWorkArea()
    {
        var floating = new LayoutFloatingSnapshot { X = -2560, Y = 200, Width = 1200, Height = 800, Scaling = 2 };
        var restored = LayoutWindowBounds.Clamp(floating, [new(0, 40, 1920, 1040, 1)]);

        Assert.InRange(restored.X, 0, 720);
        Assert.InRange(restored.Y, 40, 280);
        Assert.True(restored.X + restored.Width <= 1920);
        Assert.True(restored.Y + restored.Height <= 1080);
        Assert.Equal(1, restored.Scaling);
    }

    [Fact]
    public void ExistingNegativeCoordinateDisplayAndFractionalDpiRemainVisible()
    {
        var floating = new LayoutFloatingSnapshot { X = -1600, Y = 100, Width = 700, Height = 500, Scaling = 1.5 };
        var restored = LayoutWindowBounds.Clamp(floating, [new(-1920, 0, 1920, 1080, 1.5), new(0, 0, 2560, 1440, 2)]);

        Assert.Equal(-1600, restored.X);
        Assert.Equal(100, restored.Y);
        Assert.Equal(700, restored.Width);
        Assert.Equal(500, restored.Height);
        Assert.Equal(1.5, restored.Scaling);
    }

    [Fact]
    public void OversizedWindowShrinksToAWorkAreaWithoutLosingTitleAccess()
    {
        var floating = new LayoutFloatingSnapshot { X = 3000, Y = 2500, Width = 5000, Height = 3000, Scaling = 2 };
        var restored = LayoutWindowBounds.Clamp(floating, [new(0, 30, 1440, 870, 2)]);

        Assert.Equal(0, restored.X);
        Assert.Equal(30, restored.Y);
        Assert.Equal(720, restored.Width);
        Assert.Equal(435, restored.Height);
    }
}
