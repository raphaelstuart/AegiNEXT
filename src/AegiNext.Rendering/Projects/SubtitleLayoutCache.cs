namespace AegiNext.Rendering.Projects;

internal sealed class SubtitleLayoutCache
{
    private const int MAXIMUM_LAYOUTS = 256;
    private readonly Dictionary<SubtitleLayoutKey, LinkedListNode<SubtitleLayoutCacheEntry>> entries = [];
    private readonly Dictionary<Guid, SubtitleLayoutKey> animated = [];
    private readonly LinkedList<SubtitleLayoutCacheEntry> recent = new();

    internal int Count => entries.Count;

    internal bool TryGet(SubtitleLayoutKey key, out SubtitleLayout layout)
    {
        if (entries.TryGetValue(key, out var entry))
        {
            recent.Remove(entry);
            recent.AddLast(entry);
            layout = entry.Value.Layout;
            return true;
        }
        layout = null!;
        return false;
    }

    internal void Add(SubtitleLayoutKey key, SubtitleLayout layout, Guid? animatedLayerId)
    {
        if (animatedLayerId is { } id)
        {
            if (animated.TryGetValue(id, out var previous))
            {
                Remove(entries[previous]);
            }
            animated[id] = key;
        }
        entries.Add(key, recent.AddLast(new SubtitleLayoutCacheEntry(key, layout, animatedLayerId)));
        while (entries.Count > MAXIMUM_LAYOUTS)
        {
            Remove(recent.First!);
        }
    }

    internal void Clear()
    {
        foreach (var entry in recent)
        {
            entry.Layout.Dispose();
        }
        entries.Clear();
        animated.Clear();
        recent.Clear();
    }

    private void Remove(LinkedListNode<SubtitleLayoutCacheEntry> entry)
    {
        entries.Remove(entry.Value.Key);
        if (entry.Value.AnimatedLayerId is { } id)
        {
            animated.Remove(id);
        }
        recent.Remove(entry);
        entry.Value.Layout.Dispose();
    }
}
