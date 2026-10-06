using AegiNext.Desktop.Windowing;

namespace AegiNext.Desktop.Tests.Windowing;

public sealed class WindowsChromeHitTestTests
{
    [Theory]
    [InlineData(-999, -99, 13)]
    [InlineData(-500, -99, 12)]
    [InlineData(-1, -99, 14)]
    [InlineData(-999, 200, 10)]
    [InlineData(-1, 200, 11)]
    [InlineData(-999, 499, 16)]
    [InlineData(-500, 499, 15)]
    [InlineData(-1, 499, 17)]
    [InlineData(-500, 100, 0)]
    [InlineData(-1001, 100, 0)]
    public void NativeResizeEdgesUseSignedScreenCoordinates(int x, int y, int expected)
    {
        var point = new WindowsPoint { X = x, Y = y };
        var outer = new WindowsRect { Left = -1000, Top = -100, Right = 0, Bottom = 500 };

        var hit = WindowsChromeGeometry.HitResizeFrame(point, outer, new(8, 8, 8, 8), true, false);

        Assert.Equal(expected, (int)hit);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MaximizedAndNonResizableWindowsHaveNoResizeEdges(bool canResize, bool maximized)
    {
        var point = new WindowsPoint { X = 1, Y = 1 };
        var outer = new WindowsRect { Right = 1000, Bottom = 600 };

        Assert.Equal(WindowsChromeHitTest.NONE,
            WindowsChromeGeometry.HitResizeFrame(point, outer, new(8, 8, 8, 8), canResize, maximized));
    }

    [Theory]
    [InlineData(-1900, -900)]
    [InlineData(1900, -900)]
    [InlineData(-1900, 900)]
    [InlineData(1900, 900)]
    public void LparamCoordinatesPreserveNegativeMonitorOffsets(int x, int y)
    {
        var packed = (nint)(unchecked((ushort)(short)x) | (unchecked((uint)(ushort)(short)y) << 16));
        var point = WindowsChromeGeometry.GetScreenPoint(packed);

        Assert.Equal(x, point.X);
        Assert.Equal(y, point.Y);
    }

    [Theory]
    [InlineData(-288, -11, 8)]
    [InlineData(-239, -11, 9)]
    [InlineData(-141, -11, 20)]
    [InlineData(-320, -11, 0)]
    [InlineData(-160, -11, 0)]
    [InlineData(-100, -11, 0)]
    [InlineData(-141, 20, 0)]
    public void SystemCaptionHitUsesEachActualScreenRectangleIncludingGaps(int x, int y, int expected)
    {
        var titleBar = new WindowsTitleBarInfoEx
        {
            MinimizeBounds = new() { Left = -319, Top = -20, Right = -278, Bottom = 20 },
            MaximizeBounds = new() { Left = -277, Top = -20, Right = -180, Bottom = 20 },
            CloseBounds = new() { Left = -159, Top = -20, Right = -100, Bottom = 20 }
        };

        Assert.Equal(expected, (int)WindowsChromeGeometry.HitCaptionButtons(new() { X = x, Y = y }, in titleBar));
    }

    [Theory]
    [InlineData(0x00000001U)]
    [InlineData(0x00008000U)]
    [InlineData(0x00010000U)]
    public void NativeUnavailableInvisibleAndOffscreenButtonsDoNotBecomeCustomClickRegions(uint state)
    {
        var titleBar = new WindowsTitleBarInfoEx
        {
            MaximizeBounds = new() { Left = 100, Top = 0, Right = 190, Bottom = 50 },
            MaximizeState = state
        };

        Assert.Equal(WindowsChromeHitTest.NONE,
            WindowsChromeGeometry.HitCaptionButtons(new() { X = 145, Y = 25 }, in titleBar));
    }
}
