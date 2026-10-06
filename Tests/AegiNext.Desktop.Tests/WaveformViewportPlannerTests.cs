using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Tests;

public sealed class WaveformViewportPlannerTests
{
    [Theory]
    [InlineData(48, 1, 512)]
    [InlineData(480, 1, 64)]
    [InlineData(480, 2, 32)]
    [InlineData(20000, 1, 2)]
    [InlineData(20000, 2, 1)]
    public void ResolutionFollowsPhysicalPixelDensity(double pixelsPerSecond, double scaling, int expectedSamplesPerBucket)
    {
        var viewport = new TimelineViewport(100, pixelsPerSecond, Width: 800);
        var plan = WaveformViewportPlanner.Create(viewport, scaling, new(21600));

        Assert.NotNull(plan);
        Assert.Equal(expectedSamplesPerBucket, plan.Visible.SamplesPerBucket);
        Assert.True(plan.Analysis.Start <= plan.Visible.Start);
        Assert.True(plan.Analysis.End >= plan.Visible.End);
        Assert.InRange(plan.Analysis.BucketCount, 1, WaveformAnalysisRequest.MAX_BUCKET_COUNT);
    }

    [Fact]
    public void WideAndLongViewsStayBoundedAndCoverTheVisibleMedia()
    {
        var viewport = new TimelineViewport(0, 0.25, Width: 100000);
        var plan = WaveformViewportPlanner.Create(viewport, 4, new(21600));

        Assert.NotNull(plan);
        Assert.Equal(MediaTime.Zero, plan.Visible.Start);
        Assert.True(plan.Visible.End >= new MediaTime(21600));
        Assert.InRange(plan.Analysis.BucketCount, 1, WaveformAnalysisRequest.MAX_BUCKET_COUNT);
        var overview = WaveformViewportPlanner.CreateOverview(new(21600));
        Assert.True(overview.End >= new MediaTime(21600));
        Assert.InRange(overview.BucketCount, 1, 4096);
    }

    [Fact]
    public void EmptyInvalidAndPastEndViewsDoNotRequestDecoding()
    {
        Assert.Null(WaveformViewportPlanner.Create(new(20, 48, Width: 800), 1, new(10)));
        Assert.Null(WaveformViewportPlanner.Create(new(Width: 0), 1, new(10)));
        Assert.Null(WaveformViewportPlanner.Create(new(PixelsPerSecond: double.NaN, Width: 800), 1, new(10)));
        Assert.Null(WaveformViewportPlanner.Create(new(Width: 800), 1, MediaTime.Zero));
    }

    [Fact]
    public void VerticalScrollDoesNotChangeAnalysisAndPrefetchCoversSmallPans()
    {
        var viewport = new TimelineViewport(100, 480, Width: 800);
        var plan = WaveformViewportPlanner.Create(viewport, 1, new(21600))!;
        var scrolled = WaveformViewportPlanner.Create(viewport with { VerticalOffset = 200 }, 1, new(21600));
        var panned = WaveformViewportPlanner.Create(viewport with { StartSeconds = 100.1 }, 1, new(21600))!;

        Assert.Equal(plan, scrolled);
        Assert.True(plan.Analysis.Start <= panned.Visible.Start);
        Assert.True(plan.Analysis.End >= panned.Visible.End);
    }
}
