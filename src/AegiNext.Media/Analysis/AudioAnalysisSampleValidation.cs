using System.Numerics;

namespace AegiNext.Media.Analysis;

internal static class AudioAnalysisSampleValidation
{
    internal static void EnsureFinite(ReadOnlySpan<float> samples)
    {
        var offset = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var limit = new Vector<float>(float.MaxValue);
            while (offset <= samples.Length - Vector<float>.Count)
            {
                var values = new Vector<float>(samples.Slice(offset, Vector<float>.Count));
                if (!Vector.LessThanOrEqualAll(Vector.Abs(values), limit))
                {
                    throw new InvalidDataException("PCM 包含非有限样本。");
                }
                offset += Vector<float>.Count;
            }
        }
        foreach (var sample in samples[offset..])
        {
            if (!float.IsFinite(sample))
            {
                throw new InvalidDataException("PCM 包含非有限样本。");
            }
        }
    }
}
