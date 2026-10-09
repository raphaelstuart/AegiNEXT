using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>按稳定图层标识设置字幕 Clip 蒙版，作为一个可撤销事务。</summary>
    public void SetClipMask(Guid layerId, ClipMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        EditClipMask(layerId, mask, "Set clip mask");
    }

    /// <summary>清除指定字幕 Clip 的蒙版，作为一个可撤销事务。</summary>
    public void ClearClipMask(Guid layerId)
    {
        EditClipMask(layerId, null, "Clear clip mask");
    }

    /// <summary>仅清除节点及控制柄形变轨道，保留蒙版整体变换和静态几何。</summary>
    public void ClearMaskNodeAnimation(Guid layerId)
    {
        UpdateLayer(layerId, layer =>
        {
            RequireSubtitleMaskTarget(snapshot, layer);
            var tracks = layer.Tracks.Where(track => !AnimationPropertyMetadata.IsNodeProperty(track.Property)).ToImmutableArray();
            return tracks.Length == layer.Tracks.Length ? layer : layer with { Tracks = tracks };
        });
    }

    private void EditClipMask(Guid layerId, ClipMask? mask, string label)
    {
        Apply(label, document =>
        {
            var layer = FindLayer(document.Layers, layerId);
            RequireSubtitleMaskTarget(document, layer);

            if (layer.Mask == mask)
            {
                return document;
            }

            return document with
            {
                Layers = MapLayers(document.Layers, value => value.Id == layerId ? value with
                {
                    Mask = mask,
                    Tracks = mask is null ? value.Tracks.Where(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property)).ToImmutableArray() : value.Tracks
                } : value)
            };
        });
    }

    private static void RequireSubtitleMaskTarget(ProjectDocument document, ProjectLayer layer)
    {
        if (layer.Kind != LayerKind.SUBTITLE || layer.SubtitleId is not { } subtitleId ||
            !document.Subtitles.Any(line => line.Id == subtitleId))
        {
            throw new InvalidDataException("只有具有有效字幕引用的 SUBTITLE 图层可编辑 Clip 蒙版。");
        }
    }

    private static void ValidateMaskTopologyEdits(ProjectDocument previous, ProjectDocument current)
    {
        if (previous.Layers == current.Layers)
        {
            return;
        }

        var originals = previous.Layers
            .Where(ClipMaskAnimation.IsTopologyLocked).ToDictionary(layer => layer.Id);
        if (originals.Count == 0)
        {
            return;
        }

        foreach (var layer in current.Layers)
        {
            if (originals.TryGetValue(layer.Id, out var original) && ClipMaskAnimation.IsTopologyLocked(layer) &&
                !ClipMaskAnimation.HasSameTopology(original.Mask, layer.Mask))
            {
                throw new InvalidOperationException("存在节点形变动画时不能改变蒙版拓扑；请先清除节点形变轨道。");
            }
        }
    }

}
