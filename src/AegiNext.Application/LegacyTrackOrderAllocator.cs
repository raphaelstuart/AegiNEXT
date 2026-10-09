using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal sealed class LegacyTrackOrderAllocator
{
    private readonly IReadOnlyList<ProjectLayer> clips;
    private readonly bool reverse;
    private readonly Dictionary<Guid, int> originalIndices;
    private readonly List<int>[] ownerNodes;
    private readonly List<int> trackOwners = [];
    private readonly List<HashSet<int>> successors = [];
    private readonly List<HashSet<int>> predecessors = [];
    private readonly int[] blocked;
    private int generation;

    internal LegacyTrackOrderAllocator(IReadOnlyList<ProjectLayer> clips, IReadOnlyList<Guid> originalTracks, bool reverse = false)
    {
        this.clips = clips;
        this.reverse = reverse;
        originalIndices = originalTracks.Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index);
        ownerNodes = originalTracks.Select(_ => new List<int>()).ToArray();
        blocked = new int[originalTracks.Count + clips.Count];
        for (var index = 0; index < originalTracks.Count; index++)
        {
            AddTrack(index);
        }
    }

    internal (int[] ClipTracks, int[] TrackOrder, int[] TrackOwners) Allocate()
    {
        var overlap = new LegacyClipOverlapIndex(clips);
        var assignments = new int[clips.Count];
        for (var offset = 0; offset < clips.Count; offset++)
        {
            var index = reverse ? clips.Count - offset - 1 : offset;
            var clip = clips[index];
            var previous = overlap.GetPredecessors(clip.Start, clip.End);
            var original = originalIndices[clip.TrackId];
            var candidates = ownerNodes[original];
            var preferred = candidates[0];
            var target = successors[preferred].Count == 0 && !previous.Contains(preferred) ? preferred : -1;
            if (target < 0)
            {
                MarkAncestors(previous);
                target = candidates.FirstOrDefault(node => blocked[node] != generation, -1);
            }
            if (target < 0)
            {
                target = AddTrack(original);
            }

            foreach (var previousTrack in previous)
            {
                if (successors[previousTrack].Add(target))
                {
                    predecessors[target].Add(previousTrack);
                }
            }
            assignments[index] = target;
            overlap.Assign(clip.Start, clip.End, target);
        }

        return (assignments, SortFrontToBack(), trackOwners.ToArray());
    }

    private int AddTrack(int original)
    {
        var node = trackOwners.Count;
        trackOwners.Add(original);
        successors.Add([]);
        predecessors.Add([]);
        ownerNodes[original].Add(node);
        return node;
    }

    private void MarkAncestors(HashSet<int> previous)
    {
        generation++;
        var pending = new Stack<int>(previous);
        while (pending.TryPop(out var node))
        {
            if (blocked[node] == generation)
            {
                continue;
            }

            blocked[node] = generation;
            foreach (var earlier in predecessors[node])
            {
                pending.Push(earlier);
            }
        }
    }

    private int[] SortFrontToBack()
    {
        var remaining = successors.Select(nodes => nodes.Count).ToArray();
        var ready = new PriorityQueue<int, (int Owner, int Node)>();
        for (var node = 0; node < remaining.Length; node++)
        {
            if (remaining[node] == 0)
            {
                ready.Enqueue(node, Priority(node));
            }
        }

        var result = new List<int>(remaining.Length);
        while (ready.TryDequeue(out var node, out _))
        {
            result.Add(node);
            foreach (var earlier in predecessors[node])
            {
                remaining[earlier]--;
                if (remaining[earlier] == 0)
                {
                    ready.Enqueue(earlier, Priority(earlier));
                }
            }
        }
        if (result.Count != remaining.Length)
        {
            throw new InvalidDataException("旧工程叠覆顺序无法转换为无环轨道顺序。");
        }

        if (reverse)
        {
            result.Reverse();
        }

        return result.ToArray();
    }

    private (int Owner, int Node) Priority(int node)
    {
        return reverse ? (-trackOwners[node], -node) : (trackOwners[node], node);
    }
}
