namespace AegiNext.Rendering.Tests.Reference;

internal sealed record SubtitleReferenceFrame(int Width, int Height, float[] Pixels)
{
    internal double Energy(int channel)
    {
        var result = 0d;
        for (var index = channel; index < Pixels.Length; index += 4)
        {
            result += Pixels[index];
        }
        return result;
    }
    internal double ActiveRatio => Energy(1) / Math.Max(0.000001, Energy(0) + Energy(1));
    internal double MaximumAlpha => Pixels.Where((_, index) => index % 4 == 3).Max();
    internal (int Left, int Top, int Right, int Bottom) InkBounds()
    {
        var left = Width;
        var top = Height;
        var right = -1;
        var bottom = -1;
        for (var index = 0; index < Width * Height; index++)
        {
            if (Pixels[index * 4 + 3] <= 0.01f)
            {
                continue;
            }
            left = Math.Min(left, index % Width);
            right = Math.Max(right, index % Width);
            top = Math.Min(top, index / Width);
            bottom = Math.Max(bottom, index / Width);
        }
        return (left, top, right, bottom);
    }
}
