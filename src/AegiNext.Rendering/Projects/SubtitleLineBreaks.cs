using System.Text;
using Uax14Net;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleLineBreaks(int[] Preferred, int[] Emergency)
{
    internal static SubtitleLineBreaks Create(string text, int[] graphemes)
    {
        var boundaries = graphemes.ToHashSet();
        boundaries.Add(text.Length);
        var preferred = new List<int>();
        foreach (var opportunity in LineBreaker.Enumerate(text.AsSpan()))
        {
            if (boundaries.Contains(opportunity.Position))
            {
                preferred.Add(opportunity.Position);
            }
        }
        var emergency = new List<int>();
        var start = 0;
        var boundaryIndex = 0;
        foreach (var end in preferred)
        {
            var glued = false;
            foreach (var rune in text.AsSpan(start, end - start).EnumerateRunes())
            {
                glued |= IsGlue(rune);
            }
            while (boundaryIndex < graphemes.Length && graphemes[boundaryIndex] < end)
            {
                var boundary = graphemes[boundaryIndex++];
                if (!glued && boundary > start)
                {
                    emergency.Add(boundary);
                }
            }
            emergency.Add(end);
            start = end;
        }
        return new([.. preferred], [.. emergency]);
    }

    private static bool IsGlue(Rune rune)
    {
        return rune.Value is 0x00a0 or 0x202f or 0x2060 or 0xfeff;
    }
}
