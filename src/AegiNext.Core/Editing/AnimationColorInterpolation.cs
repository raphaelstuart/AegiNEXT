using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal static class AnimationColorInterpolation
{
    internal static double Interpolate(double start, double end, double fraction, AnimationColorSpace space, int component)
    {
        if (fraction == 0 || start == end)
        {
            return start;
        }
        if (fraction == 1)
        {
            return end;
        }
        if (space == AnimationColorSpace.LINEAR_RGB || component == 3)
        {
            return start + (end - start) * fraction;
        }
        var encodedStart = Encode(start);
        return Decode(encodedStart + (Encode(end) - encodedStart) * fraction);
    }

    private static double Encode(double value)
    {
        var magnitude = Math.Abs(value);
        var encoded = magnitude <= 0.0031308 ? magnitude * 12.92 : 1.055 * Math.Pow(magnitude, 1d / 2.4) - 0.055;
        return Math.CopySign(encoded, value);
    }

    private static double Decode(double value)
    {
        var magnitude = Math.Abs(value);
        var decoded = magnitude <= 0.04045 ? magnitude / 12.92 : Math.Pow((magnitude + 0.055) / 1.055, 2.4);
        return Math.CopySign(decoded, value);
    }
}
