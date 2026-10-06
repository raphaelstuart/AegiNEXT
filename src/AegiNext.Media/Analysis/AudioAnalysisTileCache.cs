namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisTileCache(long maximumBytes)
{
    private readonly LinkedList<AudioAnalysisTile> entries = new();
    private readonly Dictionary<AudioAnalysisTileKey, LinkedListNode<AudioAnalysisTile>> byKey = [];
    internal long Bytes { get; private set; }

    internal AudioAnalysisTile? Find(AudioAnalysisTileKey key)
    {
        if (!byKey.TryGetValue(key, out var entry))
        {
            return null;
        }
        entries.Remove(entry);
        entries.AddFirst(entry);
        return entry.Value;
    }

    internal void Add(AudioAnalysisTile tile)
    {
        if (tile.Bytes > maximumBytes)
        {
            return;
        }
        if (byKey.Remove(tile.Key, out var previous))
        {
            entries.Remove(previous);
            Bytes -= previous.Value.Bytes;
        }
        while (Bytes + tile.Bytes > maximumBytes && entries.Last is { } oldest)
        {
            byKey.Remove(oldest.Value.Key);
            Bytes -= oldest.Value.Bytes;
            entries.RemoveLast();
        }
        entries.AddFirst(tile);
        byKey.Add(tile.Key, entries.First!);
        Bytes += tile.Bytes;
    }

    internal void Clear()
    {
        entries.Clear();
        byKey.Clear();
        Bytes = 0;
    }
}
