using System.Numerics;

namespace AegiNext.Media.Analysis;

internal static class AudioWaveformPeakReducer
{
    internal static (float Minimum, float Maximum) Reduce(ReadOnlySpan<float> samples)
    {
        var minimum = Vector<float>.Zero;
        var maximum = Vector<float>.Zero;
        var offset = 0;
        if (Vector.IsHardwareAccelerated)
        {
            while (offset <= samples.Length - Vector<float>.Count)
            {
                var values = new Vector<float>(samples.Slice(offset, Vector<float>.Count));
                minimum = Vector.Min(minimum, values);
                maximum = Vector.Max(maximum, values);
                offset += Vector<float>.Count;
            }
        }
        var low = 0F;
        var high = 0F;
        for (var index = 0; index < Vector<float>.Count; index++)
        {
            low = Math.Min(low, minimum[index]);
            high = Math.Max(high, maximum[index]);
        }
        foreach (var sample in samples[offset..])
        {
            low = Math.Min(low, sample);
            high = Math.Max(high, sample);
        }
        return (low, high);
    }
}
