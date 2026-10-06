using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Editing;

internal static class WaveformViewportPlanner
{
    private const int OVERVIEW_BUCKET_COUNT = 4096;

    internal static WaveformViewportPlan? Create(TimelineViewport viewport, double renderScaling, MediaTime duration)
    {
        if (duration <= MediaTime.Zero || !double.IsFinite(viewport.StartSeconds) ||
            !double.IsFinite(viewport.PixelsPerSecond) || viewport.PixelsPerSecond <= 0 ||
            !double.IsFinite(viewport.Width) || viewport.Width <= 0)
        {
            return null;
        }

        var seconds = (double)duration.Numerator / duration.Denominator;
        var startSeconds = Math.Max(0, viewport.StartSeconds);
        var endSeconds = Math.Min(seconds, startSeconds + viewport.VisibleDuration);
        if (startSeconds >= endSeconds)
        {
            return null;
        }

        var scaling = double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1;
        var samplesPerPixel = WaveformAnalyzer.SAMPLE_RATE / (viewport.PixelsPerSecond * scaling);
        var exponent = (int)Math.Clamp(Math.Floor(Math.Log2(samplesPerPixel)), 0, 30);
        var samplesPerBucket = 1 << exponent;
        var mediaEnd = duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var visibleStart = (long)Math.Floor(startSeconds * WaveformAnalyzer.SAMPLE_RATE);
        var visibleEnd = Math.Min(mediaEnd, (long)Math.Ceiling(endSeconds * WaveformAnalyzer.SAMPLE_RATE));
        var padding = Math.Max(1, (visibleEnd - visibleStart) / 2);
        var analysisStart = Math.Max(0, visibleStart - padding);
        var analysisEnd = Math.Min(mediaEnd, visibleEnd + padding);
        while (BucketCount(analysisStart, analysisEnd, samplesPerBucket) > WaveformAnalysisRequest.MAX_BUCKET_COUNT)
        {
            samplesPerBucket = checked(samplesPerBucket * 2);
        }

        return new(Request(visibleStart, visibleEnd, samplesPerBucket), Request(analysisStart, analysisEnd, samplesPerBucket));
    }

    internal static WaveformAnalysisRequest CreateOverview(MediaTime duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, MediaTime.Zero);
        var samples = duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var samplesPerBucket = 1;
        while (BucketCount(0, samples, samplesPerBucket) > OVERVIEW_BUCKET_COUNT)
        {
            samplesPerBucket = checked(samplesPerBucket * 2);
        }
        return Request(0, samples, samplesPerBucket);
    }

    private static WaveformAnalysisRequest Request(long start, long end, int samplesPerBucket)
    {
        return new(new(start, WaveformAnalyzer.SAMPLE_RATE), samplesPerBucket, (int)BucketCount(start, end, samplesPerBucket));
    }

    private static long BucketCount(long start, long end, int samplesPerBucket)
    {
        var aligned = start - start % samplesPerBucket;
        return (end - aligned + samplesPerBucket - 1) / samplesPerBucket;
    }
}
