using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Core.Projects;

/// <summary>绑定不可变工程快照的片段和轨道索引；归属仅由片段保存，绘制从下方轨道到上方轨道。</summary>
public sealed class ProjectClipIndex
{
    private readonly FrozenDictionary<Guid, ProjectLayer> clipsById;
    private readonly FrozenDictionary<Guid, ProjectLayer> clipsBySubtitleId;
    private readonly FrozenDictionary<Guid, ImmutableArray<ProjectLayer>> clipsByTrackId;
    private readonly FrozenDictionary<Guid, ImmutableArray<SubtitleLine>> subtitlesByTrackId;

    /// <summary>为已验证工程快照构造可复用索引，不持有可变编辑器。</summary>
    public ProjectClipIndex(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        clipsById = document.Layers.ToFrozenDictionary(clip => clip.Id);
        clipsBySubtitleId = document.Layers.Where(clip => clip.SubtitleId.HasValue)
            .ToFrozenDictionary(clip => clip.SubtitleId!.Value);
        clipsByTrackId = document.Layers.GroupBy(clip => clip.TrackId)
            .ToFrozenDictionary(group => group.Key, group => group.OrderBy(clip => clip.Start).ToImmutableArray());
        subtitlesByTrackId = document.Subtitles.GroupBy(line => GetSubtitleTrackId(line.Id))
            .ToFrozenDictionary(group => group.Key, group => group.OrderBy(line => line.Start).ToImmutableArray());
        LayersInDrawingOrder = document.Tracks.Reverse().SelectMany(track => GetTrackClips(track.Id)).ToImmutableArray();
    }

    public ProjectDocument Document { get; }
    public ImmutableArray<ProjectLayer> LayersInDrawingOrder { get; }

    /// <summary>取得具有指定稳定标识的片段。</summary>
    public ProjectLayer GetClip(Guid clipId)
    {
        return clipsById[clipId];
    }

    /// <summary>按稳定标识查询片段，缺少片段时返回 false。</summary>
    public bool TryGetClip(Guid clipId, [NotNullWhen(true)] out ProjectLayer? clip)
    {
        return clipsById.TryGetValue(clipId, out clip);
    }

    /// <summary>取得字幕对应的唯一片段，不存在时抛出引用错误。</summary>
    public ProjectLayer GetSubtitleClip(Guid subtitleId)
    {
        return clipsBySubtitleId[subtitleId];
    }

    /// <summary>取得字幕片段的所属轨道。</summary>
    public Guid GetSubtitleTrackId(Guid subtitleId)
    {
        return GetSubtitleClip(subtitleId).TrackId;
    }

    /// <summary>取得轨道上按起点排列的全部类型片段。</summary>
    public ImmutableArray<ProjectLayer> GetTrackClips(Guid trackId)
    {
        return clipsByTrackId.GetValueOrDefault(trackId, []);
    }

    /// <summary>取得轨道上的字幕载荷；图片和形状不参与字幕样式与文字工作流。</summary>
    public ImmutableArray<SubtitleLine> GetTrackSubtitles(Guid trackId)
    {
        return subtitlesByTrackId.GetValueOrDefault(trackId, []);
    }
}
