using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>更新指定轨道的默认样式及现有全部字幕；效果层、时间和逐字高亮保持原样。</summary>
    public static ProjectDocument SetSubtitleTrackStyle(ProjectDocument document, Guid trackId,
        Guid presetId, string presetName, SubtitleStyle style)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var track = document.SubtitleTracks[index] with
        {
            DefaultStyle = style,
            StylePresetId = presetId,
            StylePresetName = presetName
        };
        if (track == document.SubtitleTracks[index] &&
            document.Subtitles.Where(line => line.TrackId == trackId).All(line => line.Style == style))
        {
            return document;
        }

        return Verified(document with
        {
            SubtitleTracks = document.SubtitleTracks.SetItem(index, track),
            Subtitles = document.Subtitles.Select(line => line.TrackId == trackId
                ? line with { Style = style } : line).ToImmutableArray()
        });
    }

    /// <summary>原子设置工程全部字幕和全部轨道的默认样式，不修改合成顺序。</summary>
    public static ProjectDocument SetAllSubtitleTrackStyles(ProjectDocument document,
        Guid presetId, string presetName, SubtitleStyle style)
    {
        ProjectValidator.Validate(document);
        if (document.SubtitleTracks.All(track => track.DefaultStyle == style && track.StylePresetId == presetId &&
                track.StylePresetName == presetName) && document.Subtitles.All(line => line.Style == style))
        {
            return document;
        }

        return Verified(document with
        {
            SubtitleTracks = document.SubtitleTracks.Select(track => track with
            {
                DefaultStyle = style,
                StylePresetId = presetId,
                StylePresetName = presetName
            }).ToImmutableArray(),
            Subtitles = document.Subtitles.Select(line => line with { Style = style }).ToImmutableArray()
        });
    }

    /// <summary>新增字幕轨道并验证完整快照，不重新创建任何图层。</summary>
    public static ProjectDocument AddSubtitleTrack(ProjectDocument document, SubtitleTrack track)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(track);
        return Verified(document with { SubtitleTracks = document.SubtitleTracks.Add(track) });
    }

    /// <summary>重命名存在的字幕轨道，无变化时保留原快照。</summary>
    public static ProjectDocument RenameSubtitleTrack(ProjectDocument document, Guid trackId, string name)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var track = document.SubtitleTracks[index];
        return track.Name == name ? document : Verified(document with
        {
            SubtitleTracks = document.SubtitleTracks.SetItem(index, track with { Name = name })
        });
    }

    /// <summary>仅删除空的非最后字幕轨道，避免隐式删除或转移片段。</summary>
    public static ProjectDocument RemoveSubtitleTrack(ProjectDocument document, Guid trackId)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        if (document.SubtitleTracks.Length == 1 || document.Subtitles.Any(line => line.TrackId == trackId))
        {
            throw new InvalidOperationException("只能删除空轨道，且工程必须保留至少一条字幕轨道。");
        }

        return Verified(document with { SubtitleTracks = document.SubtitleTracks.RemoveAt(index) });
    }

    /// <summary>只改变字幕轨道显示顺序，合成图层的稳定身份和顺序保持原样。</summary>
    public static ProjectDocument MoveSubtitleTrack(ProjectDocument document, Guid trackId, int newIndex)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, document.SubtitleTracks.Length);
        return index == newIndex ? document : document with
        {
            SubtitleTracks = document.SubtitleTracks.RemoveAt(index).Insert(newIndex, document.SubtitleTracks[index])
        };
    }

    /// <summary>转移字幕所属轨道，不修改字幕时间或效果层；同轨重叠会整体拒绝。</summary>
    public static ProjectDocument MoveSubtitleToTrack(ProjectDocument document, Guid subtitleId, Guid trackId)
    {
        ProjectValidator.Validate(document);
        TrackIndex(document, trackId);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        return line.TrackId == trackId ? document : Verified(document with
        {
            Subtitles = document.Subtitles.SetItem(index, line with { TrackId = trackId })
        });
    }

    /// <summary>一次性修改片段的目标轨道及时间，支持移动、裁剪和拉伸；任一碰撞不返回部分修改。</summary>
    public static ProjectDocument MoveSubtitleClip(ProjectDocument document, Guid subtitleId, Guid trackId,
        MediaTime start, MediaTime end, TimelineEditMode mode, bool move)
    {
        ProjectValidator.Validate(document);
        TrackIndex(document, trackId);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        if (start >= end || !Enum.IsDefined(mode) || move && end - start != line.End - line.Start)
        {
            throw new ArgumentException("片段区间或模式无效，整体移动必须保持时长。", nameof(end));
        }
        if (line.TrackId == trackId && line.Start == start && line.End == end)
        {
            return document;
        }

        var karaoke = line.Karaoke;
        if (!move && mode == TimelineEditMode.STRETCH)
        {
            karaoke = karaoke.Select(segment => segment with
            {
                Start = ScaleTrackTime(segment.Start, end - start, line.End - line.Start),
                End = ScaleTrackTime(segment.End, end - start, line.End - line.Start)
            }).ToImmutableArray();
        }

        return Verified(document with
        {
            Subtitles = document.Subtitles.SetItem(index, line with { TrackId = trackId, Start = start, End = end, Karaoke = karaoke }),
            Layers = MapTrackLayers(document.Layers, layer => layer.SubtitleId != subtitleId ? layer : move
                ? layer with { Start = start, End = end }
                : LayerAnimationTiming.Retime(layer, start, end, mode))
        });
    }

    private static int TrackIndex(ProjectDocument document, Guid id)
    {
        for (var index = 0; index < document.SubtitleTracks.Length; index++)
        {
            if (document.SubtitleTracks[index].Id == id)
            {
                return index;
            }
        }

        throw new KeyNotFoundException("字幕轨道不存在。");
    }

    private static ImmutableArray<ProjectLayer> MapTrackLayers(ImmutableArray<ProjectLayer> layers, Func<ProjectLayer, ProjectLayer> edit)
    {
        ImmutableArray<ProjectLayer>.Builder? changed = null;
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var children = MapTrackLayers(layer.Children, edit);
            var next = edit(children == layer.Children ? layer : layer with { Children = children });
            if (next != layer)
            {
                changed ??= layers.ToBuilder();
                changed[index] = next;
            }
        }

        return changed?.ToImmutable() ?? layers;
    }

    private static MediaTime ScaleTrackTime(MediaTime time, MediaTime newDuration, MediaTime oldDuration)
    {
        var numerator = (BigInteger)time.Numerator * newDuration.Numerator * oldDuration.Denominator;
        var denominator = (BigInteger)time.Denominator * newDuration.Denominator * oldDuration.Numerator;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }
}
