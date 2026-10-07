namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisTileCache
{
    private const long SUMMARY_BYTES = 16L * 1024 * 1024;
    private readonly AudioAnalysisTileLruCache pcm;
    private readonly AudioAnalysisTileLruCache summaries;
    private readonly long maximumBytes;

    internal AudioAnalysisTileCache(long maximumBytes)
    {
        var summaryBytes = Math.Min(maximumBytes, SUMMARY_BYTES);
        this.maximumBytes = maximumBytes;
        summaries = new(summaryBytes);
        pcm = new(maximumBytes);
    }

    internal long Bytes => pcm.Bytes + summaries.Bytes;

    internal AudioAnalysisTile? Find(AudioAnalysisTileKey key)
    {
        return (key.Kind == AudioAnalysisTileKind.PCM ? pcm : summaries).Find(key);
    }

    internal void Add(AudioAnalysisTile tile)
    {
        if (tile.Key.Kind == AudioAnalysisTileKind.PCM)
        {
            pcm.Add(tile, maximumBytes - summaries.Bytes);
        }
        else
        {
            summaries.Add(tile);
            pcm.TrimTo(maximumBytes - summaries.Bytes);
        }
    }

    internal void Clear()
    {
        pcm.Clear();
        summaries.Clear();
    }
}
