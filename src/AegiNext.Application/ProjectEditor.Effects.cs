using System.Collections.Immutable;
using AegiNext.Core.Effects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>按目标片段编译并原子应用脚本；保留内容身份、路径和未涉及的属性轨道。</summary>
    public void ApplyEffectScript(Guid layerId, EffectScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        UpdateLayer(layerId, layer =>
        {
            var style = layer.SubtitleId is { } id ? snapshot.Subtitles.First(line => line.Id == id).Style : null;
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
    }
}
