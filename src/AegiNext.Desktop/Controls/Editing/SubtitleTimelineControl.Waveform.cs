using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private WaveformData? waveform;
    private WaveformData? waveformOverview;
    private MediaTime? waveformMediaDuration;
    private StreamGeometry? waveformGeometry;
    private bool waveformGeometryDirty = true;
    private Rect waveformProjectionBody;
    private double waveformProjectionStart;
    private double waveformProjectionScale;
    private double waveformProjectionRenderScaling;
    private double waveformProjectionOrigin;

    /// <summary>接收独立局部峰值、全局回退峰值和音频范围，不改变播放头或工程快照。</summary>
    public void SetWaveform(WaveformData? detail, WaveformData? overview, MediaTime mediaDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mediaDuration, MediaTime.Zero);
        if (ReferenceEquals(waveform, detail) && ReferenceEquals(waveformOverview, overview) &&
            waveformMediaDuration == mediaDuration)
        {
            return;
        }

        waveform = detail;
        waveformOverview = overview;
        waveformMediaDuration = mediaDuration;
        waveformGeometryDirty = true;
        InvalidateVisual();
    }

    private void DrawWaveform(DrawingContext context, Rect body)
    {
        var host = TopLevel.GetTopLevel(this);
        var renderScaling = host?.RenderScaling ?? 1;
        var origin = host is null ? 0 : this.TranslatePoint(new(0, 0), host)?.X ?? 0;
        if (waveformGeometryDirty || waveformProjectionBody != body || !waveformProjectionStart.Equals(ViewStart) ||
            !waveformProjectionScale.Equals(PixelsPerSecond) || !waveformProjectionRenderScaling.Equals(renderScaling) ||
            !waveformProjectionOrigin.Equals(origin))
        {
            waveformProjectionBody = body;
            waveformProjectionStart = ViewStart;
            waveformProjectionScale = PixelsPerSecond;
            waveformProjectionRenderScaling = renderScaling;
            waveformProjectionOrigin = origin;
            waveformGeometry = BuildWaveformGeometry(body, renderScaling, origin);
            waveformGeometryDirty = false;
        }

        if (waveformGeometry is { } geometry)
        {
            context.DrawGeometry(waveformBrush, null, geometry);
        }
    }

    private StreamGeometry? BuildWaveformGeometry(Rect body, double renderScaling, double origin)
    {
        var mediaDuration = Seconds(waveformMediaDuration ?? spectrum?.Duration ?? MediaTime.Zero);
        if (body.Width <= 0 || body.Height <= 0 || mediaDuration <= ViewStart ||
            waveform is null && waveformOverview is null && spectrum is null)
        {
            return null;
        }

        var geometry = new StreamGeometry();
        using var drawing = geometry.Open();
        var firstColumn = (int)Math.Floor((body.Left + origin) * renderScaling);
        var lastColumn = (int)Math.Ceiling((Math.Min(body.Right, X(mediaDuration)) + origin) * renderScaling);
        var center = body.Center.Y;
        for (var column = firstColumn; column < lastColumn; column++)
        {
            var left = Math.Max(body.Left, column / renderScaling - origin);
            var right = Math.Min(Math.Min(body.Right, (column + 1) / renderScaling - origin), X(mediaDuration));
            var start = Math.Max(0, ViewStart + (left - body.Left) / PixelsPerSecond);
            var end = Math.Min(mediaDuration, ViewStart + (right - body.Left) / PixelsPerSecond);
            if (end <= start || right <= left)
            {
                continue;
            }

            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            var detailStart = waveform is { } detail ? Math.Max(start, Seconds(detail.Start)) : end;
            var detailEnd = waveform is { } local ? Math.Min(end, Seconds(local.End)) : start;
            if (detailEnd > detailStart)
            {
                AggregateWaveform(waveform!, detailStart, detailEnd, ref minimum, ref maximum);
                AggregateWaveformOverview(start, detailStart, ref minimum, ref maximum);
                AggregateWaveformOverview(detailEnd, end, ref minimum, ref maximum);
            }
            else
            {
                AggregateWaveformOverview(start, end, ref minimum, ref maximum);
            }

            if (!float.IsFinite(minimum))
            {
                continue;
            }

            var top = center - Math.Clamp(maximum, -1, 1) * body.Height * 0.4;
            var bottom = center - Math.Clamp(minimum, -1, 1) * body.Height * 0.4;
            if (bottom - top < 1 / renderScaling)
            {
                var middle = (top + bottom) / 2;
                top = middle - 0.5 / renderScaling;
                bottom = middle + 0.5 / renderScaling;
            }

            drawing.BeginFigure(new(left, top));
            drawing.LineTo(new(right, top));
            drawing.LineTo(new(right, bottom));
            drawing.LineTo(new(left, bottom));
            drawing.EndFigure(true);
        }

        return geometry;
    }

    private void AggregateWaveformOverview(double start, double end, ref float minimum, ref float maximum)
    {
        if (end <= start)
        {
            return;
        }

        if (waveformOverview is { } overview)
        {
            AggregateWaveform(overview, start, end, ref minimum, ref maximum);
        }
        else if (spectrum is { } fallback && !fallback.Waveform.IsEmpty)
        {
            AggregateWaveformBuckets(fallback.Waveform.Span, Seconds(fallback.Start), Seconds(fallback.ColumnDuration),
                fallback.Width, start, end, ref minimum, ref maximum);
        }
    }

    private static void AggregateWaveform(WaveformData value, double start, double end, ref float minimum, ref float maximum)
    {
        AggregateWaveformBuckets(value.Peaks.Span, Seconds(value.Start),
            value.SamplesPerBucket / (double)value.PcmSampleRate, value.BucketCount, start, end, ref minimum, ref maximum);
    }

    private static void AggregateWaveformBuckets(ReadOnlySpan<float> peaks, double start, double bucketDuration, int bucketCount,
        double rangeStart, double rangeEnd, ref float minimum, ref float maximum)
    {
        var end = start + bucketDuration * bucketCount;
        rangeStart = Math.Max(start, rangeStart);
        rangeEnd = Math.Min(end, rangeEnd);
        if (rangeEnd <= rangeStart)
        {
            return;
        }

        var first = Math.Clamp((int)Math.Floor((rangeStart - start) / bucketDuration), 0, bucketCount);
        var last = Math.Clamp((int)Math.Ceiling((rangeEnd - start) / bucketDuration), 0, bucketCount);
        for (var index = first; index < last; index++)
        {
            minimum = Math.Min(minimum, peaks[index * 2]);
            maximum = Math.Max(maximum, peaks[index * 2 + 1]);
        }
    }
}
