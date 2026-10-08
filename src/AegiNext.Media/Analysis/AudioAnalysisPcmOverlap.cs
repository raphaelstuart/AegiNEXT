namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisPcmOverlap(int count)
{
    internal float[] Samples { get; } = new float[count];
    internal long FirstSample { get; set; }
    internal bool HasSamples { get; set; }
}
