using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class WaveformCache(int maximumBytes = 32 * 1024 * 1024)
{
    private const int MAX_ENTRIES = 256;
    private readonly LinkedList<WaveformData> entries = new();
    private readonly Dictionary<WaveformAnalysisRequest, LinkedListNode<WaveformData>> byRequest = [];

    internal int MemoryBytes { get; private set; }

    internal WaveformData? Find(WaveformAnalysisRequest request)
    {
        for (var node = entries.First; node is not null; node = node.Next)
        {
            var data = node.Value;
            if (data.SamplesPerBucket == request.SamplesPerBucket && data.Start <= request.Start && data.End >= request.End)
            {
                entries.Remove(node);
                entries.AddFirst(node);
                return data;
            }
        }
        return null;
    }

    internal void Add(WaveformData data)
    {
        var bytes = data.Peaks.Length * sizeof(float);
        if (bytes > maximumBytes)
        {
            return;
        }
        if (byRequest.Remove(data.Request, out var previous))
        {
            MemoryBytes -= previous.Value.Peaks.Length * sizeof(float);
            entries.Remove(previous);
        }
        while (MemoryBytes + bytes > maximumBytes || entries.Count >= MAX_ENTRIES)
        {
            var oldest = entries.Last!;
            MemoryBytes -= oldest.Value.Peaks.Length * sizeof(float);
            byRequest.Remove(oldest.Value.Request);
            entries.RemoveLast();
        }
        byRequest.Add(data.Request, entries.AddFirst(data));
        MemoryBytes += bytes;
    }

    internal void Clear()
    {
        entries.Clear();
        byRequest.Clear();
        MemoryBytes = 0;
    }
}
