using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>一次准备整批字幕的新独立轨道；保留样式与来源顺序，将重叠区间分到必要轨道。</summary>
    public static ProjectDocument ImportSubtitleLines(ProjectDocument document, IEnumerable<SubtitleLine> lines, string name)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ProjectValidator.ValidateText(name);
        var imported = lines.Cast<SubtitleLine?>().Take(100001)
            .Select(line => SubtitleKaraokeNormalization.Normalize(line ?? throw new InvalidDataException("导入字幕不能为 null。"))).ToArray();
        if (imported.Length == 0)
        {
            return document;
        }
        if (imported.Length > 100000 || document.Subtitles.Length + imported.Length > 100000 || name.Length > 1024)
        {
            throw new InvalidDataException("字幕导入数量或轨道名称超过预算。");
        }
        var tracks = ImmutableArray.CreateBuilder<SubtitleTrack>();
        var available = new PriorityQueue<int, (MediaTime End, int Slot)>();
        foreach (var item in imported.Select((line, index) => (Line: line, Index: index)).OrderBy(item => item.Line.Start).ThenBy(item => item.Index))
        {
            int slot;
            if (available.TryPeek(out var nextSlot, out var next) && next.End <= item.Line.Start)
            {
                slot = nextSlot;
                available.Dequeue();
            }
            else
            {
                slot = tracks.Count;
                if (document.SubtitleTracks.Length + tracks.Count >= 10000)
                {
                    throw new InvalidDataException("导入轨道数量超过预算。");
                }
                tracks.Add(new() { Name = slot == 0 ? name : $"{name} ({slot + 1})", AutoApplyStyle = false });
            }
            available.Enqueue(slot, (item.Line.End, slot));
            imported[item.Index] = item.Line with { TrackId = tracks[slot].Id };
        }
        return Verified(document with
        {
            SubtitleTracks = document.SubtitleTracks.AddRange(tracks),
            Subtitles = document.Subtitles.AddRange(imported),
            Layers = document.Layers.AddRange(imported.Select(line => new ProjectLayer
            {
                Id = line.Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                Start = line.Start, End = line.End
            }))
        });
    }
}
