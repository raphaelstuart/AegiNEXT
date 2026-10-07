using System.Numerics;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Editing;

internal static class TimelineQuantization
{
    internal static MediaTime Quantize(MediaTime value, MediaTime step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, MediaTime.Zero);

        var numerator = (BigInteger)value.Numerator * step.Denominator;
        var denominator = (BigInteger)value.Denominator * step.Numerator;
        var count = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (BigInteger.Abs(remainder) * 2 >= denominator)
        {
            count += numerator.Sign;
        }

        var resultNumerator = count * step.Numerator;
        var divisor = BigInteger.GreatestCommonDivisor(resultNumerator, step.Denominator);
        return new(checked((long)(resultNumerator / divisor)), checked((long)(step.Denominator / divisor)));
    }

    internal static MediaTime Snap(MediaTime value, IReadOnlyList<MediaTime> boundaries, double pixelsPerSecond)
    {
        return ResolveSnap(value, boundaries, pixelsPerSecond).Value;
    }

    internal static TimelineSnapResult ResolveSnap(MediaTime value, IReadOnlyList<MediaTime> boundaries, double pixelsPerSecond,
        MediaTime? additionalBoundary = null)
    {
        var result = value;
        MediaTime? target = null;
        var distance = 8d;
        var boundaryCount = boundaries.Count + (additionalBoundary.HasValue ? 1 : 0);
        for (var index = 0; index < boundaryCount; index++)
        {
            var boundary = index < boundaries.Count ? boundaries[index] : additionalBoundary!.Value;
            var difference = Math.Abs(Seconds(boundary - value)) * pixelsPerSecond;
            if (difference <= distance)
            {
                result = boundary;
                target = boundary;
                distance = difference;
            }
        }

        return new(result, target);
    }

    internal static MediaTime SnapOffset(MediaTime start, MediaTime end, IReadOnlyList<MediaTime> boundaries,
        double pixelsPerSecond)
    {
        return ResolveSnapOffset(start, end, boundaries, pixelsPerSecond).Value;
    }

    internal static TimelineSnapResult ResolveSnapOffset(MediaTime start, MediaTime end, IReadOnlyList<MediaTime> boundaries,
        double pixelsPerSecond, MediaTime? additionalBoundary = null)
    {
        var result = MediaTime.Zero;
        MediaTime? target = null;
        var distance = 8d;
        ReadOnlySpan<MediaTime> edges = [start, end];
        var boundaryCount = boundaries.Count + (additionalBoundary.HasValue ? 1 : 0);
        for (var index = 0; index < boundaryCount; index++)
        {
            var boundary = index < boundaries.Count ? boundaries[index] : additionalBoundary!.Value;
            foreach (var edge in edges)
            {
                var offset = boundary - edge;
                var difference = Math.Abs(Seconds(offset)) * pixelsPerSecond;
                if (difference < distance)
                {
                    result = offset;
                    target = boundary;
                    distance = difference;
                }
            }
        }

        return new(result, target);
    }

    private static double Seconds(MediaTime value) => (double)value.Numerator / value.Denominator;
}
