using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.SubtitleFormats;

/// <summary>导出前的显式能力损失分析。</summary>
public static class SubtitleFormatLossAnalysis
{
    /// <summary>SRT 只保留纯文本及时间；列出被舍弃的排版、富文本、卡拉 OK 和合成效果。</summary>
    public static ImmutableArray<SubtitleFormatDiagnostic> ForSrt(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        foreach (var line in document.Subtitles)
        {
            if (!line.InlineSpans.IsEmpty || !line.Karaoke.IsEmpty || line.Style != new SubtitleStyle())
            {
                diagnostics.Add(new("Srt.Appearance", "SRT 仅保留文字和时间，局部样式、卡拉 OK 与排版将被舍弃。", SubtitleId: line.Id));
            }
        }
        foreach (var layer in document.Layers)
        {
            if (layer.Mask is not null)
            {
                diagnostics.Add(new("Srt.Mask", "SRT 不支持裁切蒙版，导出时蒙版几何将被舍弃。", SubtitleId: layer.SubtitleId));
            }
            if (!layer.Tracks.IsEmpty)
            {
                diagnostics.Add(new("Srt.Animation", "SRT 不支持字幕及蒙版动画，导出时动画轨道将被舍弃。", SubtitleId: layer.SubtitleId));
            }
        }
        AddCompositionLoss(document, diagnostics);
        return diagnostics.ToImmutable();
    }

    internal static void AddCompositionLoss(ProjectDocument document, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, bool supportsMasks = false)
    {
        foreach (var layer in document.Layers)
        {
            if (layer.Kind != LayerKind.SUBTITLE ||
                layer.Tracks.Any(track => !supportsMasks || !AnimationPropertyMetadata.IsMaskProperty(track.Property)) || layer.MotionPath is not null || !supportsMasks && layer.Mask is not null ||
                layer.Transform != new LayerTransform() || !layer.Opacity.Equals(1d) || layer.Blend != BlendMode.NORMAL)
            {
                diagnostics.Add(new("Subtitle.Composition", "字幕格式不能保留项目合成、动画或图形图层。", SubtitleId: layer.SubtitleId));
            }
        }
    }

}
