using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal sealed class LegacyClipOverlapIndex
{
    private const int EMPTY = -1;
    private const int MIXED = -2;
    private readonly MediaTime[] boundaries;
    private readonly int[] owners;

    internal LegacyClipOverlapIndex(IReadOnlyList<ProjectLayer> clips)
    {
        if (clips.Any(clip => clip.Start >= clip.End))
        {
            throw new InvalidDataException("旧片段的结束时间必须晚于开始时间。");
        }

        boundaries = clips.SelectMany(clip => new[] { clip.Start, clip.End }).Distinct().Order().ToArray();
        owners = new int[Math.Max(1, boundaries.Length * 4)];
        Array.Fill(owners, EMPTY);
    }

    internal HashSet<int> GetPredecessors(MediaTime start, MediaTime end)
    {
        var result = new HashSet<int>();
        Collect(1, 0, boundaries.Length - 2, Array.BinarySearch(boundaries, start),
            Array.BinarySearch(boundaries, end) - 1, result);
        return result;
    }

    internal void Assign(MediaTime start, MediaTime end, int owner)
    {
        Assign(1, 0, boundaries.Length - 2, Array.BinarySearch(boundaries, start),
            Array.BinarySearch(boundaries, end) - 1, owner);
    }

    private void Collect(int node, int left, int right, int start, int end, HashSet<int> result)
    {
        if (owners[node] != MIXED)
        {
            if (owners[node] != EMPTY)
            {
                result.Add(owners[node]);
            }
            return;
        }

        var middle = left + (right - left) / 2;
        if (start <= middle)
        {
            Collect(node * 2, left, middle, start, end, result);
        }
        if (end > middle)
        {
            Collect(node * 2 + 1, middle + 1, right, start, end, result);
        }
    }

    private void Assign(int node, int left, int right, int start, int end, int owner)
    {
        if (start <= left && right <= end)
        {
            owners[node] = owner;
            return;
        }

        if (owners[node] != MIXED)
        {
            owners[node * 2] = owners[node];
            owners[node * 2 + 1] = owners[node];
        }
        var middle = left + (right - left) / 2;
        if (start <= middle)
        {
            Assign(node * 2, left, middle, start, end, owner);
        }
        if (end > middle)
        {
            Assign(node * 2 + 1, middle + 1, right, start, end, owner);
        }

        owners[node] = owners[node * 2] == owners[node * 2 + 1] ? owners[node * 2] : MIXED;
    }
}
