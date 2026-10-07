using System.Collections.Immutable;
using AegiNext.Core.Effects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>按目标片段编译并原子应用脚本；保留内容身份、路径和未涉及的属性轨道。</summary>
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
                var tracks = EffectScriptCompiler.Compile(script, layer, style);
                if (layer.Tracks.Any(track => track.IsOrdered && tracks.Any(added => added.Target == track.Target)))
                {
                    throw new EffectScriptException("目标轨道采用有序变换；请显式清除该轨道后再应用关键帧脚本。");
                }

                return layer with
                {
                    Tracks = layer.Tracks.Where(track => !tracks.Any(added => added.Target == track.Target)).Concat(tracks)
                        .OrderBy(track => track.Target.Property).ThenBy(track => track.Target.NodeId).ToImmutableArray()
                };
            });
            if (remaining.Count > 0)
            {
                throw new KeyNotFoundException("图层不存在。");
            }

            return document with { Layers = layers };
        });
    }
}
