using System.Numerics;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal static class KaraokeTimingMath
{
    internal static MediaTime Interpolate(MediaTime start, MediaTime end, int index, int count)
    {
        if (index == 0)
        {
            return start;
        }
        if (index == count)
        {
            return end;
        }
        var numerator = (BigInteger)start.Numerator * (count - index) * end.Denominator +
            (BigInteger)end.Numerator * index * start.Denominator;
        var denominator = (BigInteger)start.Denominator * end.Denominator * count;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }
}
