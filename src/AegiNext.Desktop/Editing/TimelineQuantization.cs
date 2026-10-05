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

    internal static TimelineSnapResult ResolveSnap(MediaTime value, IReadOnlyList<MediaTime> boundaries, double pixelsPerSecond)
    {
        var result = value;
        MediaTime? target = null;
        var distance = 8d;
        foreach (var boundary in boundaries)
        {
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
        double pixelsPerSecond)
    {
        var result = MediaTime.Zero;
        MediaTime? target = null;
        var distance = 8d;
        ReadOnlySpan<MediaTime> edges = [start, end];
        foreach (var boundary in boundaries)
        {
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
