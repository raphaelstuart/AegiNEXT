using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

internal sealed class TimelineVisibleClipIndex
{
    private readonly ProjectLayer[] clips;
    private readonly int[] drawingOrder;
    private readonly MediaTime[] maximumEnds;

    internal TimelineVisibleClipIndex(IReadOnlyList<ProjectLayer> source)
    {
        var ordered = source.Select((clip, index) => (Clip: clip, Index: index)).OrderBy(item => item.Clip.Start).ToArray();
        clips = ordered.Select(item => item.Clip).ToArray();
        drawingOrder = ordered.Select(item => item.Index).ToArray();
        maximumEnds = new MediaTime[Math.Max(1, clips.Length * 4)];
        if (clips.Length > 0)
        {
            Build(1, 0, clips.Length);
        }
    }

    internal int LastVisitedNodeCount { get; private set; }

    internal IReadOnlyList<ProjectLayer> Query(MediaTime start, MediaTime end)
    {
        LastVisitedNodeCount = 0;
        if (clips.Length == 0 || end <= start)
        {
            return [];
        }

        var result = new List<int>();
        Query(1, 0, clips.Length, start, end, result);
        result.Sort((first, second) => drawingOrder[first].CompareTo(drawingOrder[second]));
        return result.Select(index => clips[index]).ToArray();
    }

    private MediaTime Build(int node, int first, int after)
    {
        if (after - first == 1)
        {
            return maximumEnds[node] = clips[first].End;
        }

        var middle = first + (after - first) / 2;
        var left = Build(node * 2, first, middle);
        var right = Build(node * 2 + 1, middle, after);
        return maximumEnds[node] = left >= right ? left : right;
    }

    private void Query(int node, int first, int after, MediaTime start, MediaTime end, List<int> result)
    {
        LastVisitedNodeCount++;
        if (maximumEnds[node] <= start || clips[first].Start >= end)
        {
            return;
        }
        if (after - first == 1)
        {
            result.Add(first);
            return;
        }

        var middle = first + (after - first) / 2;
        Query(node * 2, first, middle, start, end, result);
        Query(node * 2 + 1, middle, after, start, end, result);
    }
}
