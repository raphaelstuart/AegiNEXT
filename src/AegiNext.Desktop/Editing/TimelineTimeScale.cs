using System.Globalization;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Editing;

internal static class TimelineTimeScale
{
    internal static double MajorStep(double pixelsPerSecond)
    {
        var desired = 90 / Math.Max(0.000001, pixelsPerSecond);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(desired)));
        var units = desired / magnitude;
        return magnitude * (units <= 1 ? 1 : units <= 2 ? 2 : units <= 5 ? 5 : 10);
    }

    internal static MediaTime MinorStep(double pixelsPerSecond)
    {
        var seconds = Math.Max(0.001, MajorStep(pixelsPerSecond) / 5);
        return new((long)Math.Round(seconds * 1000000), 1000000);
    }

    internal static string Label(double seconds, double majorStep)
    {
        var value = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000));
        var format = majorStep < 1
            ? seconds >= 3600 ? @"hh\:mm\:ss\.fff" : @"mm\:ss\.fff"
            : seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
