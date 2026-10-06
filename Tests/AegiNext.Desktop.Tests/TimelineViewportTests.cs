using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineViewportTests
{
    [Fact]
    public void ZoomKeepsTheTimeUnderThePointerAndPanClampsAtBothEnds()
    {
        var viewport = new TimelineViewport(10, 40, 30, 400, 100);
        var zoomed = viewport.ZoomAt(2, 160, 100, 600);
        Assert.Equal(14, zoomed.StartSeconds + 160 / zoomed.PixelsPerSecond, 10);
        Assert.Equal(12, zoomed.StartSeconds);
        Assert.Equal(80, zoomed.PixelsPerSecond);
        Assert.Equal(30, zoomed.VerticalOffset);
        var end = zoomed.Pan(100000, 100000, 100, 600);
        Assert.Equal(95, end.StartSeconds);
        Assert.Equal(500, end.VerticalOffset);
        var start = end.Pan(-100000, -100000, 100, 600);
        Assert.Equal(0, start.StartSeconds);
        Assert.Equal(0, start.VerticalOffset);
    }

    [Fact]
    public void NormalizingAViewportAtTheEndKeepsItsFullWindowWithinTheProject()
    {
        var viewport = new TimelineViewport(95, 80, 500, 400, 100);
        var resized = (viewport with { Width = 800, Height = 200 }).Normalize(100, 600);
        Assert.Equal(90, resized.StartSeconds);
        Assert.Equal(400, resized.VerticalOffset);
        Assert.Equal(100, resized.StartSeconds + resized.VisibleDuration);
    }

    [Fact]
    public void ResizingKeepsScrollAndZoomWhileTheNextUserPanClampsToTheProject()
    {
        var viewport = new TimelineViewport(95, 80, 500, 400, 100);
        var resized = viewport.Resize(800, 200, 100, 600);
        Assert.Equal(95, resized.StartSeconds);
        Assert.Equal(80, resized.PixelsPerSecond);
        Assert.Equal(400, resized.VerticalOffset);
        Assert.Equal(105, resized.StartSeconds + resized.VisibleDuration);

        var restored = resized.Resize(400, 100, 100, 600);
        Assert.Equal(95, restored.StartSeconds);
        Assert.Equal(80, restored.PixelsPerSecond);
        var panned = resized.Pan(80, 0, 100, 600);
        Assert.Equal(90, panned.StartSeconds);
        Assert.Equal(100, panned.StartSeconds + panned.VisibleDuration);
    }

    [Fact]
    public void InvalidGestureNumbersCannotPoisonTheViewport()
    {
        var viewport = new TimelineViewport(double.NaN, double.PositiveInfinity, double.NegativeInfinity, 400, 100);
        var normalized = viewport.Normalize(60, 200);
        Assert.True(double.IsFinite(normalized.StartSeconds));
        Assert.True(double.IsFinite(normalized.PixelsPerSecond));
        Assert.Equal(0, normalized.VerticalOffset);
        Assert.Same(normalized, normalized.ZoomAt(double.NaN, 40, 60, 200));
        Assert.Same(normalized, normalized.ZoomAt(0, 40, 60, 200));
    }

    [Fact]
    public void FinestZoomRetainsPointerTimeAndProvidesOneMillisecondMinorSteps()
    {
        var viewport = new TimelineViewport(5, 2000, Width: 400, Height: 100);
        var zoomed = viewport.ZoomAt(100, 200, 20, 100);
        Assert.Equal(TimelineViewport.MAX_PIXELS_PER_SECOND, zoomed.PixelsPerSecond);
        Assert.Equal(5.1, zoomed.StartSeconds + 200 / zoomed.PixelsPerSecond, 8);
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(1, 1000), TimelineTimeScale.MinorStep(zoomed.PixelsPerSecond));
    }
}
