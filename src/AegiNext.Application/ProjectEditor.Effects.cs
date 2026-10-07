using AegiNext.Core.Effects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>按目标片段编译并原子组合脚本；保留未声明区间的既有动画、内容身份和路径。</summary>
    public void ApplyEffectScript(Guid layerId, EffectScript script)
    {
        ApplyEffectScript([layerId], script);
    }

    /// <summary>按每个目标片段的时长和基础值编译并一次提交全部脚本轨道；任一目标失败不修改快照或历史。</summary>
    public void ApplyEffectScript(IReadOnlyCollection<Guid> layerIds, EffectScript script)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        ArgumentNullException.ThrowIfNull(script);
        var selected = layerIds.ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }

        Apply("Apply effect script", document =>
        {
            var remaining = selected.ToHashSet();
            var styles = document.Subtitles.ToDictionary(line => line.Id, line => line.Style);
            var layers = MapLayers(document.Layers, layer =>
            {
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

            return document with { Layers = layers };
        });
    }
}
