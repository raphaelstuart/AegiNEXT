using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

internal static class TimelineClipRangeProjection
{
    internal static IReadOnlyList<TimelineClipRange> Partition(IEnumerable<TimelineClipRange> ranges)
    {
        var events = new SortedDictionary<MediaTime, (int Inactive, int Selected, int Invalid)>();
        foreach (var range in ranges)
        {
            if (range.Start >= range.End)
            {
                continue;
            }

            Add(events, range.Start, range, 1);
            Add(events, range.End, range, -1);
        }

        var result = new List<TimelineClipRange>();
        var counts = (Inactive: 0, Selected: 0, Invalid: 0);
        MediaTime? previous = null;
        foreach (var (time, change) in events)
        {
            if (previous is { } start && counts.Inactive + counts.Selected + counts.Invalid > 0)
            {
                var next = new TimelineClipRange(start, time, counts.Selected > 0 || counts.Invalid > 0, counts.Invalid > 0);
                if (result.Count > 0 && result[^1].End == start &&
                    result[^1].IsSelected == next.IsSelected && result[^1].IsInvalid == next.IsInvalid)
                {
                    result[^1] = result[^1] with { End = time };
                }
                else
                {
                    result.Add(next);
                }
            }

            counts = (counts.Inactive + change.Inactive, counts.Selected + change.Selected, counts.Invalid + change.Invalid);
            previous = time;
        }

        return result;
    }

    private static void Add(SortedDictionary<MediaTime, (int Inactive, int Selected, int Invalid)> events,
        MediaTime time, TimelineClipRange range, int delta)
    {
        var counts = events.GetValueOrDefault(time);
        events[time] = range.IsInvalid ? (counts.Inactive, counts.Selected, counts.Invalid + delta)
            : range.IsSelected ? (counts.Inactive, counts.Selected + delta, counts.Invalid)
            : (counts.Inactive + delta, counts.Selected, counts.Invalid);
    }
}
