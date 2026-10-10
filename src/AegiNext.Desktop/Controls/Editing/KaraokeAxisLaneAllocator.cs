using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

internal static class KaraokeAxisLaneAllocator
{
    internal static ImmutableDictionary<Guid, int> Allocate(IEnumerable<KaraokeSegment> clips)
    {
        var result = ImmutableDictionary.CreateBuilder<Guid, int>();
        var active = new PriorityQueue<int, (MediaTime End, int Lane)>();
        var available = new PriorityQueue<int, int>();
        var laneCount = 0;
        foreach (var clip in clips.OrderBy(value => value.Start).ThenBy(value => value.End)
                     .ThenBy(value => value.Utf16Start).ThenBy(value => value.Id))
        {
            while (active.TryPeek(out _, out var priority) && priority.End <= clip.Start)
            {
                var released = active.Dequeue();
                available.Enqueue(released, released);
            }
            if (!available.TryDequeue(out var lane, out _))
            {
                lane = laneCount++;
            }
            result.Add(clip.Id, lane);
            active.Enqueue(lane, (clip.End, lane));
        }
        return result.ToImmutable();
    }
}
