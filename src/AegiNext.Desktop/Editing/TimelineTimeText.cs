using System.Globalization;
using System.Numerics;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Editing;

internal static class TimelineTimeText
{
    internal static string Format(MediaTime value)
    {
        var milliseconds = BigInteger.DivRem((BigInteger)value.Numerator * 1000, value.Denominator, out var remainder);
        if (remainder.Sign < 0)
        {
            milliseconds--;
        }

        var sign = milliseconds < 0 ? "-" : string.Empty;
        milliseconds = BigInteger.Abs(milliseconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"{sign}{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}");
    }

    internal static MediaTime Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var trimmed = text.Trim();
        if (trimmed.Length > 128)
        {
            throw new FormatException("时间文本过长。");
        }

        var negative = trimmed.StartsWith('-');
        var segments = (negative ? trimmed[1..] : trimmed).Split(':');
        if (segments.Length > 3)
        {
            throw new FormatException("时间格式：00:00:00.000。");
        }

        var secondParts = segments[^1].Split('.');
        if (secondParts.Length > 2 || secondParts.All(string.IsNullOrEmpty))
        {
            throw new FormatException("秒数无效。");
        }

        var fraction = secondParts.Length == 2 ? secondParts[1] : string.Empty;
        var denominator = BigInteger.Pow(10, fraction.Length);
        var seconds = ParseDigits(secondParts[0], true) * denominator + ParseDigits(fraction, true);
        if (segments.Length > 1)
        {
            var minutes = ParseDigits(segments[^2]);
            if (seconds >= 60 * denominator || segments.Length == 3 && minutes >= 60)
            {
                throw new FormatException("分钟和秒须在有效范围内。");
            }

            seconds += minutes * 60 * denominator;
        }

        if (segments.Length == 3)
        {
            seconds += ParseDigits(segments[0]) * 3600 * denominator;
        }

        var divisor = BigInteger.GreatestCommonDivisor(seconds, denominator);
        try
        {
            return new(checked((long)((negative ? -seconds : seconds) / divisor)), checked((long)(denominator / divisor)));
        }
        catch (OverflowException error)
        {
            throw new FormatException("时间超出可表示的精确范围。", error);
        }
    }

    private static BigInteger ParseDigits(string text, bool allowEmpty = false)
    {
        if (text.Length == 0 && allowEmpty)
        {
            return BigInteger.Zero;
        }

        if (text.Length == 0 || text.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new FormatException("时间须使用十进制数字。");
        }

        return BigInteger.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
