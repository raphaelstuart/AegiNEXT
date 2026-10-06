using AegiNext.Desktop.Windowing;
using Avalonia;

namespace AegiNext.Desktop.Tests.Windowing;

public sealed class WindowsChromeGeometryTests
{
    [Theory]
    [InlineData(96U, 337, 210)]
    [InlineData(144U, 497, 310)]
    [InlineData(192U, 657, 411)]
    public void ExplicitClientResizeExcludesNativeCaptionHeight(uint dpi, int width, int height)
    {
        var frame = new WindowsFrameInsets(8, 8, 8, 8);
        var outer = WindowsChromeGeometry.GetOuterSize(new(320.5, 201.25), dpi, frame);

        Assert.Equal(new PixelSize(width, height), outer);
    }

    [Fact]
    public void RestoredClientUsesEntireTopAndPreservesNativeSideBorders()
    {
        var outer = new WindowsRect { Left = -1900, Top = -90, Right = -900, Bottom = 610 };
        var client = WindowsChromeGeometry.GetClientRect(outer, new(8, 8, 8, 8), false);

        Assert.Equal(-1892, client.Left);
        Assert.Equal(-90, client.Top);
        Assert.Equal(-908, client.Right);
        Assert.Equal(602, client.Bottom);
    }

    [Fact]
    public void MaximizedClientExcludesOffscreenFrameOnEverySide()
    {
        var outer = new WindowsRect { Left = -8, Top = -8, Right = 1928, Bottom = 1048 };
        var client = WindowsChromeGeometry.GetClientRect(outer, new(8, 8, 8, 8), true);

        Assert.Equal(0, client.Left);
        Assert.Equal(0, client.Top);
        Assert.Equal(1920, client.Right);
        Assert.Equal(1040, client.Bottom);
    }

    [Fact]
    public void FractionalDipRoundTripDoesNotAccumulateCaptionHeight()
    {
        var frame = new WindowsFrameInsets(12, 12, 12, 12);
        var outerSize = WindowsChromeGeometry.GetOuterSize(new(320.5, 201.25), 144, frame);
        var outer = new WindowsRect { Right = outerSize.Width, Bottom = outerSize.Height };
        var client = WindowsChromeGeometry.GetClientRect(outer, frame, false);
        var restoredSize = new Size((client.Right - client.Left) / 1.5, (client.Bottom - client.Top) / 1.5);

        Assert.Equal(outerSize, WindowsChromeGeometry.GetOuterSize(restoredSize, 144, frame));
    }

    [Theory]
    [InlineData(120U, 83)]
    [InlineData(168U, 9)]
    [InlineData(240U, 166)]
    public void FractionalScalingDoesNotAddAPixelWhenRestoringMeasuredClientSize(uint dpi, int pixels)
    {
        var dipSize = pixels * WindowsChromeGeometry.STANDARD_DPI / dpi;

        Assert.Equal(pixels, WindowsChromeGeometry.ToPixels(dipSize, dpi));
    }

    [Fact]
    public void CaptionReservationConvertsWindowCoordinatesToClientDipsOnNegativeScreen()
    {
        var window = new WindowsRect { Left = -1920, Top = -100, Right = 0, Bottom = 980 };
        var origin = new WindowsPoint { X = -1908, Y = -100 };
        var buttons = new WindowsRect { Left = 1722, Top = 2, Right = 1908, Bottom = 45 };

        var insets = WindowsChromeGeometry.GetCaptionInsets(window, origin, 1896, buttons, 144);

        Assert.Equal(new Thickness(0, 0, 124, 0), insets);
    }

    [Fact]
    public void CaptionReservationCannotExtendOutsideClient()
    {
        var window = new WindowsRect { Left = -1920 };
        var origin = new WindowsPoint { X = -1908 };
        var buttons = new WindowsRect { Left = 0, Right = 1908, Bottom = 45 };

        Assert.Equal(1264, WindowsChromeGeometry.GetCaptionInsets(window, origin, 1896, buttons, 144).Right);
        buttons.Left = 2000;
        Assert.Equal(0, WindowsChromeGeometry.GetCaptionInsets(window, origin, 1896, buttons, 144).Right);
    }

    [Fact]
    public void CaptionApertureUsesMeasuredClientCoordinatesAndIncludesItsTopEdge()
    {
        var window = new WindowsRect { Left = -1920, Top = -100, Right = 0, Bottom = 980 };
        var origin = new WindowsPoint { X = -1908, Y = -100 };
        var buttons = new WindowsRect { Left = 1722, Top = 2, Right = 1908, Bottom = 45 };

        var aperture = WindowsChromeGeometry.GetCaptionAperture(window, origin, new(1264, 720), buttons, 144);

        Assert.Equal(new Rect(1140, 0, 124, 30), aperture);
    }

    [Fact]
    public void CaptionApertureClampsMaximizedBoundsToVisibleClient()
    {
        var window = new WindowsRect { Left = -8, Top = -8, Right = 1928, Bottom = 1088 };
        var origin = new WindowsPoint();
        var buttons = new WindowsRect { Left = 1782, Top = 0, Right = 1936, Bottom = 40 };

        Assert.Equal(new Rect(1774, 0, 146, 32),
            WindowsChromeGeometry.GetCaptionAperture(window, origin, new(1920, 1080), buttons, 96));
    }

    [Fact]
    public void HiddenOrUndefinedCaptionBoundsAreRejected()
    {
        Assert.False(WindowsChromeGeometry.IsUsableCaptionBounds(default));
        Assert.False(WindowsChromeGeometry.IsUsableCaptionBounds(new() { Left = -1, Right = 100, Bottom = 40 }));
        Assert.False(WindowsChromeGeometry.IsUsableCaptionBounds(new() { Left = 100, Right = 100, Bottom = 40 }));
        Assert.True(WindowsChromeGeometry.IsUsableCaptionBounds(new() { Left = 100, Right = 238, Bottom = 40 }));
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(10, double.NegativeInfinity)]
    [InlineData(0, 10)]
    [InlineData(10, -1)]
    public void InvalidClientSizesAreRejectedBeforeNativeCalls(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowsChromeGeometry.GetOuterSize(new(width, height), 96, new(8, 8, 8, 8)));
    }

    [Fact]
    public void InvalidDpiAndNativeIntegerOverflowAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsChromeGeometry.ToPixels(40, 0));
        Assert.Throws<OverflowException>(() => WindowsChromeGeometry.ToPixels(double.MaxValue, 192));
    }
}
