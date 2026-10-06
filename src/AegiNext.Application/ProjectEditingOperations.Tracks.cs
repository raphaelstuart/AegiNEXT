using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>更新指定轨道的默认样式，可选择同步现有字幕；效果层、时间和逐字高亮保持原样。</summary>
    public static ProjectDocument SetSubtitleTrackStyle(ProjectDocument document, Guid trackId,
        Guid presetId, string presetName, SubtitleStyle style, bool updateExisting = false)
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
            (!updateExisting || document.Subtitles.Where(line => line.TrackId == trackId).All(line => line.Style == style && line.InlineSpans.IsEmpty)))
        {
            return document;
        }

        return Verified(document with
        {
            SubtitleTracks = document.SubtitleTracks.SetItem(index, track),
            Subtitles = updateExisting ? document.Subtitles.Select(line => line.TrackId == trackId
                ? line with { Style = style, InlineSpans = [] } : line).ToImmutableArray() : document.Subtitles
        });
    }

    /// <summary>切换轨道新字幕的自动样式应用；不修改现有字幕或保存的默认样式。</summary>
    public static ProjectDocument SetSubtitleTrackAutoApplyStyle(ProjectDocument document, Guid trackId, bool enabled)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var track = document.SubtitleTracks[index];
        return track.AutoApplyStyle == enabled ? document : Verified(document with
        {
            SubtitleTracks = document.SubtitleTracks.SetItem(index, track with { AutoApplyStyle = enabled })
        });
    }

    /// <summary>在一个已准备资源的快照中创建字幕片段，继承启用的轨道默认值或提供的备用样式。</summary>
    public static ProjectDocument CreateSubtitleClips(ProjectDocument document, IEnumerable<SubtitleLine> lines,
        Guid trackId, SubtitleStyle? fallbackStyle = null)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(lines);
        var track = document.SubtitleTracks[TrackIndex(document, trackId)];
        var style = track.AutoApplyStyle && track.DefaultStyle is { } defaultStyle
            ? defaultStyle : fallbackStyle ?? new SubtitleStyle();
        var imported = lines.Select(line => SubtitleKaraokeNormalization.Normalize(line with { TrackId = trackId, Style = style })).ToImmutableArray();
        if (imported.IsEmpty)
        {
            return document;
        }

        return Verified(document with
        {
            Subtitles = document.Subtitles.AddRange(imported),
            Layers = document.Layers.AddRange(imported.Select(line => new ProjectLayer
            {
                Id = line.Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                Start = line.Start, End = line.End
            }))
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

    /// <summary>删除字幕轨道及其全部片段和图层；允许删除最后一条轨道，保留其他轨道和共享资源。</summary>
    public static ProjectDocument RemoveSubtitleTrack(ProjectDocument document, Guid trackId)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var subtitles = document.Subtitles.Where(line => line.TrackId == trackId).Select(line => line.Id).ToHashSet();
        var layerIds = document.Layers.SelectMany(Descendants)
            .Where(layer => layer.SubtitleId is { } id && subtitles.Contains(id))
            .Select(layer => layer.Id).ToImmutableArray();
        var result = RemoveClips(document, layerIds);
        return Verified(result with { SubtitleTracks = result.SubtitleTracks.RemoveAt(index) });
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
        var inactiveKaraoke = line.InactiveKaraoke;
        if (!move && mode == TimelineEditMode.STRETCH)
        {
            karaoke = ScaleTrackKaraoke(karaoke, end - start, line.End - line.Start);
            inactiveKaraoke = ScaleTrackKaraoke(inactiveKaraoke, end - start, line.End - line.Start);
        }

        return Verified(document with
        {
            Subtitles = document.Subtitles.SetItem(index, line with
            {
                TrackId = trackId, Start = start, End = end, Karaoke = karaoke, InactiveKaraoke = inactiveKaraoke
            }),
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

    private static ImmutableArray<KaraokeSegment> ScaleTrackKaraoke(ImmutableArray<KaraokeSegment> segments,
        MediaTime newDuration, MediaTime oldDuration)
    {
        return segments.Select(segment => segment with
        {
            Start = ScaleTrackTime(segment.Start, newDuration, oldDuration),
            End = ScaleTrackTime(segment.End, newDuration, oldDuration)
        }).ToImmutableArray();
    }
}
