using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal sealed class AssTextParser(SubtitleLine original, IReadOnlyDictionary<string, AssStyleDefinition> styles,
    SceneColor secondary, double scaleX = 1, double scaleY = 1, bool projectSource = false, int canvasWidth = 1920, int canvasHeight = 1080)
{
    private readonly AssMaskParser maskParser = new(original.End - original.Start, scaleX, scaleY, canvasWidth, canvasHeight);
    private readonly StringBuilder text = new();
    private readonly ImmutableArray<SubtitleInlineSpan>.Builder spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
    private readonly ImmutableArray<KaraokeSegment>.Builder karaoke = ImmutableArray.CreateBuilder<KaraokeSegment>();
    private readonly ImmutableArray<AssSourceMapEntry>.Builder map = ImmutableArray.CreateBuilder<AssSourceMapEntry>();
    private readonly ImmutableArray<AssKaraokeSourceMapEntry>.Builder karaokeMap = ImmutableArray.CreateBuilder<AssKaraokeSourceMapEntry>();
    private readonly ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
    private SubtitleStyle current = original.Style;
    private SubtitleStyle lineStyle = original.Style;
    private readonly SceneColor secondaryColor = secondary;
    private SceneColor? inactive;
    private SceneColor? segmentInactive;
    private SceneColor? segmentActive;
    private bool segmentInactiveVaries;
    private bool segmentActiveVaries;
    private KaraokeVisualStyleOverride? segmentActiveVisual;
    private KaraokeVisualStyleOverride? instantVisual;
    private MediaTime? instantVisualTime;
    private int instantSourceStart;
    private int instantSourceLength;
    private MediaTime karaokeTime;
    private MediaTime? segmentDuration;
    private KaraokeHighlightKind kind;
    private int segmentStart;
    private int segmentSourceStart;
    private int segmentSourceLength;
    private bool drawing;
    private bool explicitPosition;
    private bool explicitAlignment;
    private static readonly string[] knownTags = ["iclip", "alpha", "xbord", "ybord", "xshad", "yshad", "fscx", "fscy", "bord", "shad", "blur", "move", "clip", "fade", "pos", "fad", "frz", "frx", "fry", "fsp", "org", "pbo", "fn", "fs", "an", "kf", "ko", "kt", "1c", "2c", "3c", "4c", "1a", "2a", "3a", "4a", "b", "i", "u", "s", "c", "r", "k", "K", "p", "q", "t", "a"];

    internal AssTextEditResult Parse(string source)
    {
        AssFormatValues.CheckText(source);
        for (var index = 0; index < source.Length;)
        {
            if (source[index] == '{')
            {
                var end = source.IndexOf('}', index + 1);
                if (end < 0)
                {
                    throw new InvalidDataException("ASS 标签块缺少结束括号。");
                }
                ParseTags(source.AsSpan(index + 1, end - index - 1), index + 1);
                map.Add(new(index, end - index + 1, text.Length, 0));
                index = end + 1;
                continue;
            }
            var count = 1;
            var content = source[index].ToString();
            if (source[index] == '\\' && index + 1 < source.Length && source[index + 1] is 'N' or 'n' or 'h' or '{' or '}')
            {
                count = 2;
                content = source[index + 1] switch
                {
                    'N' => "\n", 'n' => " ", 'h' => "\u00a0", '{' => "{", '}' => "}",
                    _ => source.Substring(index, 2)
                };
                if (source[index + 1] is '{' or '}')
                {
                    Report("Ass.LiteralBraces", "字面花括号转义属于 libass 扩展，其他 ASS 播放器可能改变文字。", index, count);
                }
            }
            var offset = text.Length;
            if (!drawing)
            {
                if (segmentDuration > MediaTime.Zero)
                {
                    var inactiveColor = inactive ?? secondaryColor;
                    if (text.Length == segmentStart)
                    {
                        segmentInactive = inactiveColor;
                        segmentActive = current.Fill;
                        segmentActiveVisual = ResolveInstantVisual();
                    }
                    else
                    {
                        segmentInactiveVaries |= segmentInactive != inactiveColor;
                        if (!segmentActiveVaries && segmentActive != current.Fill)
                        {
                            Report("Ass.KaraokeActiveRuns", "同一演唱片段包含多个高亮颜色，采用该片段首个高亮颜色。", index, count);
                            segmentActiveVaries = true;
                        }
                    }
                }
                else if (instantVisualTime is not null)
                {
                    Report("Ass.UnsupportedTag", "ASS 瞬时描边和阴影变换需要逐字片段时间，普通文字无法保存此动画。", instantSourceStart, instantSourceLength);
                    instantVisualTime = null;
                    instantVisual = null;
                }
                text.Append(content);
                AppendStyle(offset, content.Length);
            }
            map.Add(new(index, count, offset, drawing ? 0 : content.Length));
            index += count;
        }
        FlushKaraoke();
        var line = original with
        {
            Text = text.ToString(), Style = lineStyle, InlineSpans = spans.ToImmutable(),
            Karaoke = karaoke.ToImmutable(), InactiveKaraoke = []
        };
        var contentOffset = !projectSource && !line.Karaoke.IsEmpty && line.Karaoke[0].Start < MediaTime.Zero ? -line.Karaoke[0].Start : MediaTime.Zero;
        if (contentOffset > MediaTime.Zero)
        {
            line = line with { Karaoke = line.Karaoke.Select(clip => clip with { Start = clip.Start + contentOffset, End = clip.End + contentOffset }).ToImmutableArray() };
        }
        ValidateLine(line);
        diagnostics.AddRange(maskParser.Diagnostics);
        return new(line, diagnostics.ToImmutable(), map.ToImmutable())
        {
            KaraokeSourceMap = karaokeMap.ToImmutable(), Mask = maskParser.Mask, ContentOffset = contentOffset,
            MaskTracks = maskParser.Tracks().Select(track => contentOffset == MediaTime.Zero ? track : track with
            {
                Keyframes = track.Keyframes.Select(key => key with { Time = key.Time + contentOffset }).ToImmutableArray(),
                Transforms = track.Transforms.Select(operation => operation with { Start = operation.Start + contentOffset, End = operation.End + contentOffset }).ToImmutableArray()
            }).ToImmutableArray()
        };
    }

    private void ParseTags(ReadOnlySpan<char> block, int sourceOffset)
    {
        var cursor = 0;
        while (cursor < block.Length)
        {
            if (block[cursor] != '\\')
            {
                cursor++;
                continue;
            }
            var start = cursor++;
            var valueStart = cursor;
            var depth = 0;
            while (cursor < block.Length)
            {
                if (block[cursor] == '\\' && depth == 0)
                {
                    break;
                }
                depth += block[cursor] == '(' ? 1 : block[cursor] == ')' ? -1 : 0;
                if (depth < 0)
                {
                    throw new InvalidDataException("ASS 标签括号不匹配。");
                }
                cursor++;
            }
            if (depth != 0)
            {
                throw new InvalidDataException("ASS 标签括号不匹配。");
            }
            var token = block[valueStart..cursor].ToString();
            var name = knownTags.FirstOrDefault(tag => token.StartsWith(tag, StringComparison.Ordinal));
            if (name is null)
            {
                Report("Ass.UnsupportedTag", $"不支持 ASS 标签：{token}。", sourceOffset + start, cursor - start);
                continue;
            }
            var value = token[name.Length..].Trim();
            ApplyTag(name, value, sourceOffset + start, cursor - start);
        }
    }

    private void ApplyTag(string name, string value, int sourceStart, int sourceLength)
    {
        var baseline = original.Style;
        switch (name)
        {
            case "fn":
                var family = value.Length == 0 ? baseline.FontFamily : value;
                current = current with
                {
                    FontFamily = family, FontAssetId = null,
                    FontVariant = family == current.FontFamily ? current.FontVariant : null
                };
                break;
            case "fs":
                var fontSize = value.Length == 0 ? baseline.FontSize : value[0] is '+' or '-'
                    ? current.FontSize * (1 + AssFormatValues.Number(value) / 10) : AssFormatValues.Number(value) * scaleY;
                current = current with { FontSize = fontSize <= 0 ? baseline.FontSize : fontSize };
                break;
            case "b":
                var weight = value.Length == 0 ? (baseline.Bold ? 1 : 0) : AssFormatValues.Integer(value);
                var bold = weight is -1 or 1 || weight >= 600;
                current = current with { Bold = bold, FontVariant = bold == current.Bold ? current.FontVariant : null };
                if (weight is not (-1 or 0 or 1))
                {
                    Report("Ass.FontWeight", "ASS 显式字体粗细被转换为普通或粗体。", sourceStart, sourceLength);
                }
                break;
            case "i":
                var italic = value.Length == 0 ? baseline.Italic : AssFormatValues.Integer(value) != 0;
                current = current with { Italic = italic, FontVariant = italic == current.Italic ? current.FontVariant : null };
                break;
            case "u": current = current with { Underline = value.Length == 0 ? baseline.Underline : AssFormatValues.Integer(value) != 0 }; break;
            case "s": current = current with { Strikethrough = value.Length == 0 ? baseline.Strikethrough : AssFormatValues.Integer(value) != 0 }; break;
            case "c":
            case "1c": current = current with { Fill = value.Length == 0 ? baseline.Fill : AssFormatValues.Color(value, current.Fill) }; break;
            case "2c": inactive = value.Length == 0 ? secondaryColor : AssFormatValues.Color(value, inactive ?? secondaryColor); break;
            case "3c": current = current with { Stroke = value.Length == 0 ? baseline.Stroke : AssFormatValues.Color(value, current.Stroke) }; break;
            case "4c": current = current with { ShadowColor = value.Length == 0 ? baseline.ShadowColor : AssFormatValues.Color(value, current.ShadowColor) }; break;
            case "alpha":
                var alpha = value.Length == 0 ? baseline.Fill.Alpha : AssFormatValues.Alpha(value);
                current = current with { Fill = current.Fill with { Alpha = alpha }, Stroke = current.Stroke with { Alpha = alpha }, ShadowColor = current.ShadowColor with { Alpha = alpha } };
                inactive = (inactive ?? secondaryColor) with { Alpha = alpha };
                break;
            case "1a": current = current with { Fill = current.Fill with { Alpha = value.Length == 0 ? baseline.Fill.Alpha : AssFormatValues.Alpha(value) } }; break;
            case "2a": inactive = (inactive ?? secondaryColor) with { Alpha = value.Length == 0 ? secondaryColor.Alpha : AssFormatValues.Alpha(value) }; break;
            case "3a": current = current with { Stroke = current.Stroke with { Alpha = value.Length == 0 ? baseline.Stroke.Alpha : AssFormatValues.Alpha(value) } }; break;
            case "4a": current = current with { ShadowColor = current.ShadowColor with { Alpha = value.Length == 0 ? baseline.ShadowColor.Alpha : AssFormatValues.Alpha(value) } }; break;
            case "bord": current = current with { StrokeWidth = value.Length == 0 ? baseline.StrokeWidth : AssFormatValues.Number(value) * scaleY }; break;
            case "shad":
                var shadow = value.Length == 0 ? baseline.ShadowOffset.Y : AssFormatValues.Number(value) * scaleY;
                current = current with { ShadowOffset = new(shadow * scaleX / scaleY, shadow) }; break;
            case "xshad": current = current with { ShadowOffset = current.ShadowOffset with { X = AssFormatValues.Number(value) * scaleX } }; break;
            case "yshad": current = current with { ShadowOffset = current.ShadowOffset with { Y = AssFormatValues.Number(value) * scaleY } }; break;
            case "blur":
                current = current with { ShadowBlur = value.Length == 0 ? baseline.ShadowBlur : AssFormatValues.Number(value) * scaleY };
                if (current.ShadowBlur > 0 && (current.Fill.Alpha > 0 || current.StrokeWidth > 0 && current.Stroke.Alpha > 0 || current.ShadowColor.Alpha > 0))
                {
                    Report("Ass.ShadowBlur", "ASS 的模糊作用于文字或描边边缘，转换为项目阴影模糊会改变边缘外观。", sourceStart, sourceLength);
                }
                break;
            case "r":
                current = value.Length == 0 ? baseline : styles.TryGetValue(value, out var style) ? style.Style : baseline;
                inactive = value.Length == 0 ? secondaryColor : styles.TryGetValue(value, out var reset) ? reset.Secondary : secondaryColor;
                instantVisual = null;
                instantVisualTime = null;
                if (value.Length > 0 && !styles.ContainsKey(value))
                {
                    Report("Ass.UnknownStyle", $"未找到重置样式 {value}，使用当前行样式。", sourceStart, sourceLength);
                }
                break;
            case "an":
            case "a":
                var alignment = name == "an" ? AssFormatValues.Alignment(AssFormatValues.Integer(value)) :
                    AssFormatValues.LegacyAlignment(AssFormatValues.Integer(value));
                if (explicitAlignment)
                {
                    Report("Ass.DuplicatePlacement", "ASS 同一行重复的对齐标签已忽略，采用首个值。", sourceStart, sourceLength);
                    break;
                }
                explicitAlignment = true;
                lineStyle = lineStyle with
                {
                    Alignment = alignment,
                    Position = projectSource ? original.Style.Position : lineStyle.Position is { } position
                        ? explicitPosition ? position with { Pivot = Pivot(alignment) } : AssSubtitleFormat.MarginPosition(alignment, lineStyle.Margin, lineStyle.Margin, lineStyle.Margin)
                        : null
                };
                break;
            case "pos":
                if (projectSource)
                {
                    Report("Ass.ProjectPositionUnsupported", "项目 ASS 代码不支持位置标签，请移除 \\pos，并在项目原生位置属性中调整定位。", sourceStart, sourceLength);
                    break;
                }
                var coordinates = value.Trim('(', ')').Split(',');
                if (coordinates.Length != 2)
                {
                    throw new InvalidDataException("ASS pos 必须有两个坐标。");
                }
                var offset = new ScenePoint(AssFormatValues.Number(coordinates[0]) * scaleX, AssFormatValues.Number(coordinates[1]) * scaleY);
                if (explicitPosition)
                {
                    Report("Ass.DuplicatePlacement", "ASS 同一行重复的位置标签已忽略，采用首个值。", sourceStart, sourceLength);
                    break;
                }
                lineStyle = lineStyle with { Position = new() { Anchor = new(0, 0), Pivot = Pivot(lineStyle.Alignment), Offset = offset } };
                explicitPosition = true;
                break;
            case "kt":
                FlushKaraoke();
                karaokeTime = new(AssFormatValues.Integer(value), 100);
                if (projectSource && karaokeTime < MediaTime.Zero)
                {
                    throw new InvalidDataException("项目高级代码的卡拉 OK 时间不能为负。");
                }
                break;
            case "k":
            case "K":
            case "kf":
            case "ko":
                FlushKaraoke();
                var centiseconds = AssFormatValues.Integer(value);
                if (centiseconds < 0)
                {
                    throw new InvalidDataException("ASS 卡拉 OK 时长不能为负。");
                }
                segmentDuration = new(centiseconds, 100);
                segmentStart = text.Length;
                segmentSourceStart = sourceStart;
                segmentSourceLength = sourceLength;
                kind = name == "k" ? KaraokeHighlightKind.STEP : name == "ko" ? KaraokeHighlightKind.OUTLINE_STEP : KaraokeHighlightKind.SWEEP;
                break;
            case "clip":
            case "iclip":
                maskParser.Apply(name, value, original.Id, sourceStart, sourceLength);
                break;
            case "t":
                if (!maskParser.TryTransform(value, original.Id, sourceStart, sourceLength))
                {
                    ParseInstantTransform(value, sourceStart, sourceLength);
                }
                break;
            case "p":
                drawing = AssFormatValues.Integer(value) != 0;
                Report("Ass.Drawing", "ASS 绘图未导入，请使用项目图形图层。", sourceStart, sourceLength);
                break;
            default:
                Report("Ass.UnsupportedTag", name == "move" ? "ASS move 不受支持，请使用项目位置特效。" : $"ASS 标签 {name} 未导入。", sourceStart, sourceLength);
                break;
        }
    }

    private void FlushKaraoke()
    {
        if (segmentDuration is not { } duration)
        {
            return;
        }
        if (text.Length > segmentStart && duration > MediaTime.Zero)
        {
            karaokeMap.Add(new(segmentSourceStart, segmentSourceLength, karaoke.Count));
            karaoke.Add(new(segmentStart, text.Length - segmentStart, karaokeTime, karaokeTime + duration, segmentActive ?? current.Fill)
            {
                HighlightKind = kind, InactiveStyle = segmentInactiveVaries ? null : new() { Fill = segmentInactive ?? secondaryColor },
                ActiveStyle = (segmentActiveVisual ?? new()) with { Fill = segmentActive ?? current.Fill }
            });
        }
        else if (text.Length > segmentStart && !projectSource)
        {
            Report("Ass.ZeroKaraoke", "零时长演唱文字已作为普通文字导入。", 0, 0);
        }
        karaokeTime += duration;
        segmentDuration = null;
        segmentInactive = null;
        segmentActive = null;
        segmentInactiveVaries = false;
        segmentActiveVaries = false;
        segmentActiveVisual = null;
    }

    private KaraokeVisualStyleOverride? ResolveInstantVisual()
    {
        if (instantVisualTime is null)
        {
            return null;
        }
        if (instantVisualTime != karaokeTime || kind == KaraokeHighlightKind.SWEEP)
        {
            Report("Ass.UnsupportedTag", "ASS 瞬时描边和阴影变换必须与逐字或轮廓逐字片段起点对齐。", instantSourceStart, instantSourceLength);
            return null;
        }
        return instantVisual;
    }

    private void ParseInstantTransform(string value, int sourceStart, int sourceLength)
    {
        if (!value.StartsWith('(') || !value.EndsWith(')'))
        {
            throw new InvalidDataException("ASS t 必须使用括号参数。");
        }
        var parts = value[1..^1].Split(',', 3);
        if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ||
            !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var end) || start <= 0 || start != end ||
            !parts[2].TrimStart().StartsWith('\\') || instantVisual is not null)
        {
            Report("Ass.UnsupportedTag", "ASS 仅支持与逐字起点对齐的单个正时间瞬时描边和阴影变换。", sourceStart, sourceLength);
            return;
        }
        var visual = new KaraokeVisualStyleOverride();
        foreach (var token in parts[2].Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = knownTags.FirstOrDefault(tag => token.StartsWith(tag, StringComparison.Ordinal));
            var argument = name is null ? string.Empty : token[name.Length..].Trim();
            var target = visual.ApplyTo(current);
            visual = name switch
            {
                "3c" when argument.Length > 0 => visual with { Stroke = AssFormatValues.Color(argument, target.Stroke) },
                "3a" when argument.Length > 0 => visual with { Stroke = target.Stroke with { Alpha = AssFormatValues.Alpha(argument) } },
                "4c" when argument.Length > 0 => visual with { ShadowColor = AssFormatValues.Color(argument, target.ShadowColor) },
                "4a" when argument.Length > 0 => visual with { ShadowColor = target.ShadowColor with { Alpha = AssFormatValues.Alpha(argument) } },
                "bord" when argument.Length > 0 => visual with { StrokeWidth = AssFormatValues.Number(argument) * scaleY },
                "shad" when argument.Length > 0 => visual with { ShadowOffset = new(AssFormatValues.Number(argument) * scaleX, AssFormatValues.Number(argument) * scaleY) },
                "xshad" when argument.Length > 0 => visual with { ShadowOffset = target.ShadowOffset with { X = AssFormatValues.Number(argument) * scaleX } },
                "yshad" when argument.Length > 0 => visual with { ShadowOffset = target.ShadowOffset with { Y = AssFormatValues.Number(argument) * scaleY } },
                "blur" when argument.Length > 0 => visual with { ShadowBlur = AssFormatValues.Number(argument) * scaleY },
                _ => visual
            };
            if (name is not ("3c" or "3a" or "4c" or "4a" or "bord" or "shad" or "xshad" or "yshad" or "blur") || argument.Length == 0)
            {
                Report("Ass.UnsupportedTag", "ASS 瞬时变换只能包含可保存的描边和阴影标签。", sourceStart, sourceLength);
                return;
            }
        }
        instantVisual = visual;
        instantVisualTime = new(start, 1000);
        instantSourceStart = sourceStart;
        instantSourceLength = sourceLength;
    }

    private void AppendStyle(int start, int length)
    {
        var visibleStyle = segmentDuration > MediaTime.Zero ? current with { Fill = inactive ?? secondaryColor } : current;
        if (visibleStyle == original.Style)
        {
            return;
        }
        var style = SubtitleInlineStyleOverride.FromStyle(visibleStyle);
        if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == start && spans[^1].Style == style)
        {
            spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
        }
        else
        {
            spans.Add(new(start, length, style));
        }
    }

    private void Report(string code, string message, int start, int length) => diagnostics.Add(new(code, message, start, length, original.Id));

    internal static ScenePoint Pivot(TextAlignment alignment)
    {
        var row = Math.DivRem((int)alignment, 3, out var column);
        return new(column / 2.0, row / 2.0);
    }

    internal static void ValidateLine(SubtitleLine line)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(line.Text).Append(line.Text.Length).ToHashSet();
        foreach (var span in line.InlineSpans)
        {
            if (!boundaries.Contains(span.Utf16Start) || !boundaries.Contains(span.Utf16Start + span.Utf16Length))
            {
                throw new InvalidDataException("ASS 局部样式不能拆开字素。");
            }
            ProjectValidator.ValidateSubtitleStyle(span.Style.ApplyTo(line.Style));
        }
        foreach (var segment in line.Karaoke)
        {
            if (!boundaries.Contains(segment.Utf16Start) || !boundaries.Contains(segment.Utf16Start + segment.Utf16Length))
            {
                throw new InvalidDataException("ASS 卡拉 OK 标签不能拆开字素。");
            }
        }
        if (line.Text.Length > 1000000)
        {
            throw new InvalidDataException("ASS 单行文本超过预算。");
        }
        ProjectValidator.ValidateSubtitleStyle(line.Style);
    }
}
