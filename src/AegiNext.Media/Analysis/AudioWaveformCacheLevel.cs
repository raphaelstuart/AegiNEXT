namespace AegiNext.Media.Analysis;

internal sealed class AudioWaveformCacheLevel(int samplesPerBucket)
{
    internal int SamplesPerBucket { get; } = samplesPerBucket;
    internal float[] Peaks { get; } = new float[2048];
    internal long FirstBucket { get; set; }
    internal long NextBucket { get; set; }
    internal int Count { get; set; }
    internal bool HasPending { get; set; }
    internal float Minimum { get; set; }
    internal float Maximum { get; set; }
}
