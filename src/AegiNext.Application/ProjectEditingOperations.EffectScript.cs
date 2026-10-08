using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>在冻结快照上编译并组合目标片段的脚本轨道；取消或任一目标失败不发布部分结果。</summary>
    public static ProjectDocument ApplyEffectScript(ProjectDocument document, IReadOnlyCollection<Guid> layerIds,
        EffectScript script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        ArgumentNullException.ThrowIfNull(script);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(document);
        var remaining = layerIds.ToHashSet();
        if (remaining.Count == 0)
        {
            return document;
        }
        var styles = document.Subtitles.ToDictionary(line => line.Id, line => line.Style);
        var layers = MapTrackLayers(document.Layers, layer =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!remaining.Remove(layer.Id))
            {
                return layer;
            }
            var style = layer.SubtitleId is { } id ? styles[id] : null;
            var tracks = EffectScriptComposer.Compose(script, layer, style);
            return tracks == layer.Tracks ? layer : layer with { Tracks = tracks };
        });
        if (remaining.Count > 0)
        {
            throw new KeyNotFoundException("图层不存在。");
        }
        var result = layers == document.Layers ? document : document with { Layers = layers };
        ProjectValidator.Validate(result);
        return result;
    }
}
