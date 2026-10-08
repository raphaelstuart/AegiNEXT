namespace AegiNext.Media.Analysis;

internal sealed record AudioAnalysisTile(AudioAnalysisTileKey Key, ReadOnlyMemory<float> Samples)
{
    internal long Bytes => (long)Samples.Length * sizeof(float);
}
