using System.Buffers;

namespace AegiNext.Rendering.Projects;

internal static class F16LayerBlur
{
    internal static void Apply(LinearRenderSurface surface, double sigma)
    {
        if (sigma <= 0)
        {
            return;
        }

        var width = surface.Info.Width;
        var height = surface.Info.Height;
        var count = surface.Info.ChannelCount;
        var source = ArrayPool<Half>.Shared.Rent(count);
        var scratch = ArrayPool<Half>.Shared.Rent(count);
        try
        {
            surface.CopyPixels(source.AsSpan(0, count));
            var lower = (int)Math.Floor(Math.Sqrt(4 * sigma * sigma + 1));
            if (lower % 2 == 0)
            {
                lower--;
            }

            lower = Math.Max(1, lower);
            var lowerCount = (int)Math.Round((12 * sigma * sigma - 3 * lower * lower - 12 * lower - 9) / (-4 * lower - 4));
            for (var pass = 0; pass < 3; pass++)
            {
                var radius = ((pass < lowerCount ? lower : lower + 2) - 1) / 2;
                if (radius == 0)
                {
                    continue;
                }

                Box(source, scratch, width, height, radius, false);
                Box(scratch, source, width, height, radius, true);
            }

            surface.ReplacePixels(source.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<Half>.Shared.Return(source);
            ArrayPool<Half>.Shared.Return(scratch);
        }
    }

    private static void Box(Half[] source, Half[] target, int width, int height, int radius, bool vertical)
    {
        var lines = vertical ? width : height;
        var length = vertical ? height : width;
        var step = vertical ? width * 4 : 4;
        var divisor = 2.0 * radius + 1;
        for (var line = 0; line < lines; line++)
        {
            var origin = vertical ? line * 4 : line * width * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                double sum = 0;
                for (var item = 0; item <= Math.Min(radius, length - 1); item++)
                {
                    sum += (double)source[origin + item * step + channel];
                }

                for (var item = 0; item < length; item++)
                {
                    target[origin + item * step + channel] = (Half)(sum / divisor);
                    if (item - radius >= 0)
                    {
                        sum -= (double)source[origin + (item - radius) * step + channel];
                    }

                    if (item + radius + 1 < length)
                    {
                        sum += (double)source[origin + (item + radius + 1) * step + channel];
                    }
                }
            }
        }
    }
}
