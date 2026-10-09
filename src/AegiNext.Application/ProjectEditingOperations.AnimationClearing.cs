using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>整体清除所选片段的动画轨道，保留静态内容、运动路径和逐字高亮。</summary>
    public static ProjectDocument ClearAnimationTracks(ProjectDocument document, IReadOnlyCollection<Guid> layerIds)
    {
        ProjectValidator.Validate(document);
        var selection = SelectClipLayers(document, layerIds).Select(layer => layer.Id).ToHashSet();
        return ClearAnimationTracksCore(document, selection, null);
    }

    /// <summary>整体清除指定图层同一属性的全部动画目标，包含各节点及两种轨道表示；未知身份整批拒绝。</summary>
    public static ProjectDocument ClearAnimationTracks(ProjectDocument document, IReadOnlyCollection<Guid> layerIds,
        AnimationProperty property)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(layerIds);
        if (!AnimationPropertyMetadata.CurrentProperties.Contains(property))
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }
        var selection = layerIds.ToHashSet();
        if (document.Layers.Count(layer => selection.Contains(layer.Id)) != selection.Count)
        {
            throw new KeyNotFoundException("待清除动画的图层不存在。");
        }
        return ClearAnimationTracksCore(document, selection, property);
    }

    private static ProjectDocument ClearAnimationTracksCore(ProjectDocument document, HashSet<Guid> selection,
        AnimationProperty? property)
    {
        if (selection.Count == 0)
        {
            return document;
        }
        var layers = MapTrackLayers(document.Layers, layer =>
        {
            if (!selection.Contains(layer.Id) || layer.Tracks.IsEmpty ||
                property is { } selectedProperty && !layer.Tracks.Any(track => track.Property == selectedProperty))
            {
                return layer;
            }
            return layer with
            {
                Tracks = property is { } target
                    ? layer.Tracks.Where(track => track.Property != target).ToImmutableArray() : []
            };
        });
        return layers == document.Layers ? document : Verified(document with { Layers = layers });
    }
}
