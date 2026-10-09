using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal static class AssTextWriter
{
    internal static AssBodyWriteResult Write(SubtitleLine line, MediaTime origin, bool projection = false, bool preserveContentClock = false)
    {
        AssTextParser.ValidateLine(line);
        var result = new StringBuilder();
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        for (var index = 0; index < line.Text.Length - 1; index++)
        {
            if (line.Text[index] == '\\' && line.Text[index + 1] is 'N' or 'n' or 'h')
            {
                throw new InvalidDataException("ASS 无法无损表达字面反斜杠与 N、n 或 h 的相邻组合。");
            }
        }
        if (line.Text.IndexOfAny(['{', '}']) >= 0)
        {
            diagnostics.Add(new("Ass.LiteralBraces", "字面花括号使用 libass 扩展转义，其他 ASS 播放器可能改变文字。", SubtitleId: line.Id));
        }
        var boundaries = new SortedSet<int> { 0, line.Text.Length };
        foreach (var span in line.InlineSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var segment in line.Karaoke)
        {
            boundaries.Add(segment.Utf16Start);
            boundaries.Add(segment.Utf16Start + segment.Utf16Length);
        }
        var offsets = boundaries.ToArray();
        long time = 0;
        long clipStartCount = 0;
        SubtitleStyle? previous = null;
        KaraokeSegment? previousClip = null;
        var spanIndex = 0;
        var clipIndex = 0;
        var visible = line.End - line.Start;
        var visibleCount = visible.ToTimestamp(new(1, 100), MediaTimeRounding.CEILING).Value;
        if (!projection && !preserveContentClock && line.Karaoke.Any(clip => clip.Start < origin || clip.End > origin + visible))
        {
            diagnostics.Add(new("Ass.KaraokeCrop", "部分卡拉 OK 片段超出字幕可见范围，导出时裁剪片段并保留整句起止时间。", SubtitleId: line.Id));
        }
        for (var index = 0; index < offsets.Length - 1; index++)
        {
            var offset = offsets[index];
            while (spanIndex < line.InlineSpans.Length && line.InlineSpans[spanIndex].Utf16Start + line.InlineSpans[spanIndex].Utf16Length <= offset)
            {
                spanIndex++;
            }
            var span = spanIndex < line.InlineSpans.Length && line.InlineSpans[spanIndex].Utf16Start <= offset ? line.InlineSpans[spanIndex] : null;
            var style = span?.Style.ApplyTo(line.Style) ?? line.Style;
            var styleChanged = style != previous;
            while (clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start + line.Karaoke[clipIndex].Utf16Length <= offset)
            {
                clipIndex++;
            }
            var clip = clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start <= offset ? line.Karaoke[clipIndex] : null;
            if (clip is not null && !projection && !preserveContentClock && (clip.End <= origin || clip.Start >= origin + line.End - line.Start))
            {
                clip = null;
            }
            if (clip is not null && clip != previousClip && !projection && !preserveContentClock && time >= visibleCount)
            {
                throw new InvalidDataException("字幕范围内的厘秒不足以保留全部卡拉 OK 正时长，无法导出。");
            }
            var endCount = time;
            if (clip != previousClip)
            {
                if (clip is not null)
                {
                    var start = preserveContentClock ? clip.Start - origin : clip.Start < origin ? MediaTime.Zero : clip.Start - origin;
                    var end = clip.End - origin;
                    end = !projection && !preserveContentClock && end > visible ? visible : end;
                    clipStartCount = preserveContentClock ? start.ToTimestamp(new(1, 100), MediaTimeRounding.TO_EVEN).Value : Math.Max(time, start.ToTimestamp(new(1, 100), MediaTimeRounding.TO_EVEN).Value);
                    endCount = Math.Max(clipStartCount + 1, end.ToTimestamp(new(1, 100), MediaTimeRounding.TO_EVEN).Value);
                    if (!projection && !preserveContentClock && endCount > visibleCount)
                    {
                        throw new InvalidDataException("卡拉 OK 的正时长厘秒量化超出字幕范围，无法导出。");
                    }
                    if (endCount != end.ToTimestamp(new(1, 100), MediaTimeRounding.TO_EVEN).Value ||
                        !projection && (new MediaTime(clipStartCount, 100) != start || new MediaTime(endCount, 100) != end))
                    {
                        diagnostics.Add(new("Ass.KaraokeQuantization", "卡拉 OK 的正时长厘秒量化调整了片段边界。", SubtitleId: line.Id));
                    }
                }
            }
            if (styleChanged || clip != previousClip)
            {
                CheckTagBoundary(result);
                result.Append("{\\r").Append(StyleTags(style, projection)).Append('}');
                AddStyleDiagnostics(style, line.Id, diagnostics, projection);
                previous = style;
                if (clip is not null)
                {
                    var inactive = KaraokeVisualStyleResolver.ResolveInactive(style, clip);
                    var active = KaraokeVisualStyleResolver.ResolveActive(style, line.KaraokeStyle, clip);
                    AddStyleDiagnostics(inactive, line.Id, diagnostics, projection);
                    AddStyleDiagnostics(active, line.Id, diagnostics, projection);
                    result.Append("{\\2c").Append(AssFormatValues.Color(inactive.Fill, false)).Append("\\2a").Append(AssFormatValues.Alpha(inactive.Fill));
                    result.Append("\\1c").Append(AssFormatValues.Color(active.Fill, false)).Append("\\1a").Append(AssFormatValues.Alpha(active.Fill));
                    if (clip.HighlightKind == KaraokeHighlightKind.SWEEP)
                    {
                        result.Append(VisualTags(inactive, projection));
                        if (VisualsDiffer(inactive, active))
                        {
                            diagnostics.Add(new("Ass.KaraokeVisual", "ASS 的逐字扫过不能完整保留前后的独立描边和阴影，已采用未激活外观。", SubtitleId: line.Id));
                        }
                    }
                    else if (clipStartCount <= 0)
                    {
                        result.Append(VisualTags(active, projection));
                    }
                    else
                    {
                        result.Append(VisualTags(inactive, projection));
                        var changes = ChangedVisualTags(inactive, active, projection);
                        if (changes.Length > 0)
                        {
                            var startMs = checked(clipStartCount * 10).ToString(CultureInfo.InvariantCulture);
                            result.Append("\\t(").Append(startMs).Append(',').Append(startMs).Append(',').Append(changes).Append(')');
                        }
                    }
                    result.Append('}');
                }
                else
                {
                    result.Append("{\\2c").Append(AssFormatValues.Color(style.Fill, false)).Append("\\2a").Append(AssFormatValues.Alpha(style.Fill)).Append('}');
                }
            }
            if (clip != previousClip)
            {
                if (clip is not null)
                {
                    if (preserveContentClock)
                    {
                        result.Append("{\\kt").Append(clipStartCount.ToString(CultureInfo.InvariantCulture)).Append('}');
                    }
                    else if (clipStartCount > time)
                    {
                        result.Append("{\\k").Append((clipStartCount - time).ToString(CultureInfo.InvariantCulture)).Append('}');
                    }
                    var tag = clip.HighlightKind == KaraokeHighlightKind.STEP ? "k" : clip.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP ? "ko" : "kf";
                    result.Append("{\\").Append(tag).Append((endCount - clipStartCount).ToString(CultureInfo.InvariantCulture)).Append('}');
                    time = endCount;
                }
                else
                {
                    result.Append("{\\k0}");
                }
                previousClip = clip;
            }
            result.Append(Escape(line.Text[offset..offsets[index + 1]]));
            if (OutOfGamut(style.Fill) || OutOfGamut(style.Stroke) || OutOfGamut(style.ShadowColor))
            {
                diagnostics.Add(new("Ass.ColorRange", "ASS 8 位 sRGB 颜色会限制线性 HDR 或负颜色。", SubtitleId: line.Id));
            }
        }
        return new(result.ToString(), diagnostics.Distinct().ToImmutableArray(), new(time, 100));
    }

    private static string StyleTags(SubtitleStyle style, bool projection)
    {
        if (style.FontFamily.IndexOfAny(['\\', '{', '}', '\r', '\n']) >= 0)
        {
            throw new InvalidDataException("ASS 字体名包含标签控制字符。");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"\\fn{style.FontFamily}\\fs{AssFormatValues.Number(style.FontSize)}\\b{(style.Bold ? 1 : 0)}\\i{(style.Italic ? 1 : 0)}\\u{(style.Underline ? 1 : 0)}\\s{(style.Strikethrough ? 1 : 0)}\\1c{AssFormatValues.Color(style.Fill, false)}\\1a{AssFormatValues.Alpha(style.Fill)}\\3c{AssFormatValues.Color(style.Stroke, false)}\\3a{AssFormatValues.Alpha(style.Stroke)}\\4c{AssFormatValues.Color(style.ShadowColor, false)}\\4a{AssFormatValues.Alpha(style.ShadowColor)}\\bord{AssFormatValues.Number(style.StrokeWidth)}\\xshad{AssFormatValues.Number(style.ShadowOffset.X)}\\yshad{AssFormatValues.Number(style.ShadowOffset.Y)}") + (projection ? "\\blur" + AssFormatValues.Number(style.ShadowBlur) : string.Empty);
    }

    private static bool OutOfGamut(SceneColor color) => color.Red is < 0 or > 1 || color.Green is < 0 or > 1 || color.Blue is < 0 or > 1;

    private static string VisualTags(SubtitleStyle style, bool projection)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"\\3c{AssFormatValues.Color(style.Stroke, false)}\\3a{AssFormatValues.Alpha(style.Stroke)}\\4c{AssFormatValues.Color(style.ShadowColor, false)}\\4a{AssFormatValues.Alpha(style.ShadowColor)}\\bord{AssFormatValues.Number(style.StrokeWidth)}\\xshad{AssFormatValues.Number(style.ShadowOffset.X)}\\yshad{AssFormatValues.Number(style.ShadowOffset.Y)}") + (projection ? "\\blur" + AssFormatValues.Number(style.ShadowBlur) : string.Empty);
    }

    private static string ChangedVisualTags(SubtitleStyle inactive, SubtitleStyle active, bool projection)
    {
        var tags = new StringBuilder();
        if (inactive.Stroke != active.Stroke)
        {
            tags.Append("\\3c").Append(AssFormatValues.Color(active.Stroke, false)).Append("\\3a").Append(AssFormatValues.Alpha(active.Stroke));
        }
        if (!inactive.StrokeWidth.Equals(active.StrokeWidth))
        {
            tags.Append("\\bord").Append(AssFormatValues.Number(active.StrokeWidth));
        }
        if (inactive.ShadowColor != active.ShadowColor)
        {
            tags.Append("\\4c").Append(AssFormatValues.Color(active.ShadowColor, false)).Append("\\4a").Append(AssFormatValues.Alpha(active.ShadowColor));
        }
        if (!inactive.ShadowOffset.X.Equals(active.ShadowOffset.X))
        {
            tags.Append("\\xshad").Append(AssFormatValues.Number(active.ShadowOffset.X));
        }
        if (!inactive.ShadowOffset.Y.Equals(active.ShadowOffset.Y))
        {
            tags.Append("\\yshad").Append(AssFormatValues.Number(active.ShadowOffset.Y));
        }
        if (projection && !inactive.ShadowBlur.Equals(active.ShadowBlur))
        {
            tags.Append("\\blur").Append(AssFormatValues.Number(active.ShadowBlur));
        }
        return tags.ToString();
    }

    private static bool VisualsDiffer(SubtitleStyle inactive, SubtitleStyle active)
    {
        var strokeVisible = inactive.StrokeWidth > 0 && inactive.Stroke.Alpha > 0 || active.StrokeWidth > 0 && active.Stroke.Alpha > 0;
        var shadowVisible = inactive.ShadowColor.Alpha > 0 || active.ShadowColor.Alpha > 0;
        return strokeVisible && (inactive.Stroke != active.Stroke || !inactive.StrokeWidth.Equals(active.StrokeWidth)) ||
            shadowVisible && (inactive.ShadowColor != active.ShadowColor || inactive.ShadowOffset != active.ShadowOffset || !inactive.ShadowBlur.Equals(active.ShadowBlur));
    }

    private static void AddStyleDiagnostics(SubtitleStyle style, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, bool projection)
    {
        if (!projection)
        {
            AssExportPrecision.AddStyle(style, id, diagnostics);
        }
        if (!projection && style.FontVariant is not null)
        {
            diagnostics.Add(new("Ass.FontVariant", "ASS 的字体家族与粗体、斜体标记无法完整保留系统字体命名变体。", SubtitleId: id));
        }
        if (OutOfGamut(style.Fill) || OutOfGamut(style.Stroke) || OutOfGamut(style.ShadowColor))
        {
            diagnostics.Add(new("Ass.ColorRange", "ASS 8 位 sRGB 颜色会限制线性 HDR 或负颜色。", SubtitleId: id));
        }
        if (style.ShadowBlur > 0 && (style.ShadowColor.Alpha > 0 || projection && (style.Fill.Alpha > 0 || style.StrokeWidth > 0 && style.Stroke.Alpha > 0)))
        {
            diagnostics.Add(new("Ass.ShadowBlur", projection
                ? "项目的阴影模糊与 ASS 的文字边缘模糊语义不同，保留数值会改变文字或描边边缘。"
                : "ASS 无法单独表达项目的阴影模糊，导出时已省略阴影模糊。", SubtitleId: id));
        }
    }

    private static void CheckTagBoundary(StringBuilder source)
    {
        if (source.Length > 0 && source[^1] == '\\')
        {
            throw new InvalidDataException("ASS 字面反斜杠紧邻样式或卡拉 OK 标签，无法无损导出。");
        }
    }

    private static string Escape(string text) => text.Replace("{", "\\{", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal)
        .Replace("\r\n", "\\N", StringComparison.Ordinal).Replace("\r", "\\N", StringComparison.Ordinal)
        .Replace("\n", "\\N", StringComparison.Ordinal).Replace("\u00a0", "\\h", StringComparison.Ordinal);
}
