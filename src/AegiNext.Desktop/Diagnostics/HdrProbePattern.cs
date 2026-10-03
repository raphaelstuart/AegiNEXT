using System.Numerics;
using AegiNext.Media;
using AegiNext.Rendering;

namespace AegiNext.Desktop.Diagnostics;

internal static class HdrProbePattern
{
    internal static HdrFrame Create(string? fontPath)
    {
        using var surface = new LinearRenderSurface(new(960, 540, 203));
        surface.FillRectangle(Vector2.Zero, new(960, 540), new(0.025f, 0.025f, 0.025f, 1));
        ReadOnlySpan<float> stops = [0, 0.18f, 0.5f, 1, 2, 4];
        for (var i = 0; i < stops.Length; i++)
        {
            var value = stops[i];
            surface.FillRectangle(new(24 + i * 154, 26), new(142, 190), new(value, value, value, 1));
        }

        surface.FillRectangle(new(24, 242), new(294, 128), new(2, 0, 0, 1));
        surface.FillRectangle(new(332, 242), new(294, 128), new(0, 2, 0, 1));
        surface.FillRectangle(new(640, 242), new(294, 128), new(0, 0, 2, 1));
        surface.FillEllipse(new(418.25f, 300.25f), new(120.5f, 120.5f), new(4, 4, 4, 0.5f));
        if (fontPath is not null)
        {
            using var shaper = new TextShaper(File.ReadAllBytes(fontPath));
            using var text = shaper.Shape("AegiNext / Linear light", 48, TextDirection.LEFT_TO_RIGHT, "en");
            surface.DrawText(text, new(28, 480), new(2, 2, 2, 1));
        }

        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return new(surface.Info, pixels, 812);
    }
}
