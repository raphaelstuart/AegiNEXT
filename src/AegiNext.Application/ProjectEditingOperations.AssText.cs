using System.Collections.Immutable;
using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>原子提交高级 ASS 编辑的文字、蒙版与蒙版轨道，保留其他动画和内容时钟。</summary>
    public static ProjectDocument ApplyAssTextEdit(ProjectDocument document, Guid subtitleId, AssTextEditResult edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        if (edited.Line.Id != subtitleId || edited.Line.Start != document.Subtitles[index].Start || edited.Line.End != document.Subtitles[index].End)
        {
            throw new InvalidDataException("高级代码结果不能改变字幕身份或可见时间。");
        }
        var previous = SubtitleLayer(document.Layers, subtitleId);
        if (ClipMaskAnimation.IsTopologyLocked(previous) && !ClipMaskAnimation.HasSameTopology(previous.Mask, edited.Mask))
        {
            throw new InvalidOperationException("存在节点形变动画时不能从高级代码改变蒙版拓扑；请先清除节点形变轨道。");
        }
        var next = previous with
        {
            Mask = edited.Mask,
            Tracks = previous.Tracks.Where(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property)).ToImmutableArray().AddRange(edited.MaskTracks)
        };
        var line = SubtitleKaraokeNormalization.Normalize(edited.Line);
        if (line == document.Subtitles[index] && previous.Mask == next.Mask && previous.Tracks.SequenceEqual(next.Tracks))
        {
            return document;
        }
        return Verified(document with { Subtitles = document.Subtitles.SetItem(index, line), Layers = MapAssLayers(document.Layers, next) });
    }

    private static ImmutableArray<ProjectLayer> MapAssLayers(ImmutableArray<ProjectLayer> layers, ProjectLayer changed)
    {
        return layers.Select(layer => layer.Id == changed.Id ? changed : layer).ToImmutableArray();
    }
}
