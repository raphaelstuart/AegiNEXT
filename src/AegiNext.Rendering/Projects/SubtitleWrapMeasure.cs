using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleWrapMeasure(int[] Starts, SKRect[] Bounds, int TextLength)
{
    internal int FindEnd(int begin, int[] candidates, float available, out bool fits)
    {
        var candidateIndex = Array.BinarySearch(candidates, begin);
        candidateIndex = candidateIndex < 0 ? ~candidateIndex : candidateIndex + 1;
        var first = candidates[candidateIndex];
        var selected = first;
        fits = false;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        for (var index = Array.BinarySearch(Starts, begin); index < Starts.Length; index++)
        {
            minimum = Math.Min(minimum, Bounds[index].Left);
            maximum = Math.Max(maximum, Bounds[index].Right);
            if (maximum - minimum > available)
            {
                break;
            }
            var end = index + 1 < Starts.Length ? Starts[index + 1] : TextLength;
            if (end == candidates[candidateIndex])
            {
                selected = end;
                fits = true;
                if (++candidateIndex == candidates.Length)
                {
                    break;
                }
            }
        }
        return selected;
    }
}
