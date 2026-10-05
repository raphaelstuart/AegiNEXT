using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

/// <summary>ASS v4+ 交换；将不支持的效果列为损失，拒绝非法结构和时间。</summary>
public static class AssSubtitleFormat
{
    /// <summary>以目标画布重采样静态字幕布局，保留对白来源的稳定顺序。</summary>
    public static AssImportResult Parse(string source, int targetWidth = 1920, int targetHeight = 1080)
    {
        AssFormatValues.CheckText(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        var rows = source.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var styleRows = new List<Dictionary<string, string>>();
        var events = new List<Dictionary<string, string>>();
        var section = string.Empty;
        string[]? styleFormat = null;
        string[]? eventFormat = null;
        var sawInfo = false;
        var sawEvents = false;
        var attachments = false;
        foreach (var row in rows)
        {
            var trimmed = row.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';'))
            {
                continue;
            }
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed;
                sawInfo |= section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase);
                sawEvents |= section.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                attachments |= section.Equals("[Fonts]", StringComparison.OrdinalIgnoreCase) || section.Equals("[Graphics]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase) && !section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase) && !section.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var content = row.TrimStart();
            var colon = content.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidDataException("ASS 行缺少字段分隔符。");
            }
            var key = content[..colon].Trim();
            var value = content[(colon + 1)..].TrimStart();
            if (section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase))
            {
                info[key] = value;
            }
            else if (section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                {
                    styleFormat = ParseFormat(value);
                }
                else if (key.Equals("Style", StringComparison.OrdinalIgnoreCase))
                {
                    styleRows.Add(ParseFields(value, styleFormat ?? throw new InvalidDataException("ASS 样式缺少 Format。")));
                }
            }
            else if (section.Equals("[Events]", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                {
                    eventFormat = ParseFormat(value);
                    if (!eventFormat[^1].Equals("Text", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("ASS Text 必须是最后一个对白字段。");
                    }
                }
                else if (key.Equals("Dialogue", StringComparison.OrdinalIgnoreCase))
                {
                    if (events.Count >= 100000)
                    {
                        throw new InvalidDataException("ASS 对白数量超过预算。");
                    }
                    events.Add(ParseFields(value, eventFormat ?? throw new InvalidDataException("ASS 对白缺少 Format。")));
                }
            }
        }
        if (!sawInfo || !sawEvents || info.TryGetValue("ScriptType", out var scriptType) && !scriptType.Equals("v4.00+", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("仅支持含 Script Info 和 Events 的 ASS v4+ 文件。");
        }
        var hasWidth = info.TryGetValue("PlayResX", out var x);
        var hasHeight = info.TryGetValue("PlayResY", out var y);
        var width = hasWidth ? AssFormatValues.Integer(x!) : targetWidth;
        var height = hasHeight ? AssFormatValues.Integer(y!) : targetHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("ASS PlayRes 必须为正。");
        }
        var scaleX = (double)targetWidth / width;
        var scaleY = (double)targetHeight / height;
        var styles = new Dictionary<string, AssStyleDefinition>(StringComparer.Ordinal);
        var unsupportedGeometry = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fields in styleRows)
        {
            var style = ParseStyle(fields, scaleX, scaleY);
            if (!styles.TryAdd(style.Name, style))
            {
                throw new InvalidDataException("ASS 样式名称重复。");
            }
            if (!AssFormatValues.Number(Get(fields, "ScaleX", "100")).Equals(100d) || !AssFormatValues.Number(Get(fields, "ScaleY", "100")).Equals(100d) ||
                !AssFormatValues.Number(Get(fields, "Spacing", "0")).Equals(0d) || !AssFormatValues.Number(Get(fields, "Angle", "0")).Equals(0d) || Get(fields, "BorderStyle", "1") != "1")
            {
                unsupportedGeometry.Add(style.Name);
            }
        }
        var fallback = new AssStyleDefinition("Default", new() { FontSize = 20 * scaleY }, SceneColor.White);
        var lines = ImmutableArray.CreateBuilder<SubtitleLine>();
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        if (!hasWidth || !hasHeight)
        {
            diagnostics.Add(new("Ass.PlayRes", "ASS 缺失的 PlayRes 轴按目标画布尺寸解释，已声明的轴仍按其源尺寸重采样。"));
        }
        if (attachments)
        {
            diagnostics.Add(new("Ass.Attachments", "ASS 嵌入字体或图片未导入，请在工程中单独添加资源。"));
        }
        foreach (var fields in events.OrderBy(row => AssFormatValues.Integer(Get(row, "Layer", "0"))))
        {
            var name = Get(fields, "Style", "Default");
            var definition = styles.TryGetValue(name, out var declared) ? declared : fallback;
            var start = AssFormatValues.ParseTime(Required(fields, "Start"));
            var end = AssFormatValues.ParseTime(Required(fields, "End"));
            if (start >= end)
            {
                throw new InvalidDataException("ASS 结束时间必须晚于开始时间。");
            }
            var line = new SubtitleLine { Start = start, End = end, Style = definition.Style };
            var marginL = AssFormatValues.Number(Get(fields, "MarginL", "0"));
            var marginR = AssFormatValues.Number(Get(fields, "MarginR", "0"));
            var marginV = AssFormatValues.Number(Get(fields, "MarginV", "0"));
            if (marginL != 0 || marginR != 0 || marginV != 0)
            {
                line = line with { Style = line.Style with { Position = MarginPosition(line.Style.Alignment,
                    marginL == 0 ? Math.Abs(line.Style.Position?.Offset.X ?? line.Style.Margin) : marginL * scaleX,
                    marginR == 0 ? Math.Abs(line.Style.Position?.Offset.X ?? line.Style.Margin) : marginR * scaleX,
                    marginV == 0 ? line.Style.Margin : marginV * scaleY) } };
            }
            var parsed = new AssTextParser(line, styles, definition.Secondary, scaleX, scaleY).Parse(Required(fields, "Text"));
            lines.Add(SubtitleKaraokeNormalization.Normalize(parsed.Line));
            diagnostics.AddRange(parsed.Diagnostics);
            if (!styles.ContainsKey(name))
            {
                diagnostics.Add(new("Ass.UnknownStyle", $"样式 {name} 不存在，采用默认样式。", SubtitleId: line.Id));
            }
            if (Get(fields, "Effect", "").Length > 0)
            {
                diagnostics.Add(new("Ass.Effect", "ASS 对白 Effect 字段未导入。", SubtitleId: line.Id));
            }
            if (unsupportedGeometry.Contains(name))
            {
                diagnostics.Add(new("Ass.StyleGeometry", "ASS 样式的缩放、字距、旋转或背景框未导入，请使用工程特效。", SubtitleId: line.Id));
            }
        }
        return new(lines.ToImmutable(), diagnostics.ToImmutable());
    }

    /// <summary>按工程合成层顺序导出全部字幕，静态样式去重，时间显式量化到厘秒。</summary>
    public static SubtitleFormatWriteResult Write(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        var result = new StringBuilder();
        result.AppendLine("[Script Info]").AppendLine("ScriptType: v4.00+")
            .AppendLine("PlayResX: " + document.Width.ToString(CultureInfo.InvariantCulture))
            .AppendLine("PlayResY: " + document.Height.ToString(CultureInfo.InvariantCulture))
            .AppendLine("LayoutResX: " + document.Width.ToString(CultureInfo.InvariantCulture))
            .AppendLine("LayoutResY: " + document.Height.ToString(CultureInfo.InvariantCulture))
            .AppendLine("YCbCr Matrix: None").AppendLine("WrapStyle: 2").AppendLine("ScaledBorderAndShadow: yes").AppendLine();
        result.AppendLine("[V4+ Styles]").AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        var styles = new Dictionary<SubtitleStyle, string>();
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        foreach (var line in document.Subtitles)
        {
            var style = line.Style with { FontAssetId = null, Position = null };
            if (styles.ContainsKey(style))
            {
                continue;
            }
            if (style.FontFamily.Contains(',', StringComparison.Ordinal))
            {
                throw new InvalidDataException("ASS 样式字体名不能包含逗号。");
            }
            var name = "Style" + (styles.Count + 1).ToString(CultureInfo.InvariantCulture);
            styles.Add(style, name);
            result.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Style: {name},{style.FontFamily},{AssFormatValues.Number(style.FontSize)},{AssFormatValues.Color(style.Fill)},{AssFormatValues.Color(style.Fill)},{AssFormatValues.Color(style.Stroke)},{AssFormatValues.Color(style.ShadowColor)},{(style.Bold ? -1 : 0)},{(style.Italic ? -1 : 0)},{(style.Underline ? -1 : 0)},{(style.Strikethrough ? -1 : 0)},100,100,0,0,1,{AssFormatValues.Number(style.StrokeWidth)},{AssFormatValues.Number(style.ShadowOffset.Y)},{AssFormatValues.Alignment(style.Alignment)},{AssFormatValues.Number(style.Margin)},{AssFormatValues.Number(style.Margin)},{AssFormatValues.Number(style.Margin)},1"));
        }
        result.AppendLine().AppendLine("[Events]").AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        var byId = document.Subtitles.ToDictionary(line => line.Id);
        var order = 0;
        foreach (var layer in SubtitleFormatLossAnalysis.Flatten(document.Layers).Where(layer => layer.SubtitleId.HasValue))
        {
            var line = byId[layer.SubtitleId!.Value];
            if (line.Start < MediaTime.Zero)
            {
                throw new InvalidDataException("ASS 不支持负对白时间。");
            }
            var body = AssTextWriter.Write(line, layer.AnimationOffset);
            diagnostics.AddRange(body.Diagnostics);
            if (line.Style.FontAssetId.HasValue)
            {
                diagnostics.Add(new("Ass.FontResource", "ASS 文件不包含工程嵌入字体，请在播放环境安装对应字体。", SubtitleId: line.Id));
            }
            if (!line.Style.LineHeight.Equals(1.2))
            {
                diagnostics.Add(new("Ass.LineHeight", "ASS 不支持工程自定义行高。", SubtitleId: line.Id));
            }
            var position = line.Style.Position;
            var placement = "{\\an" + AssFormatValues.Alignment(line.Style.Alignment).ToString(CultureInfo.InvariantCulture);
            if (position is not null)
            {
                var pivot = AssTextParser.Pivot(line.Style.Alignment);
                if (position.Pivot != pivot)
                {
                    diagnostics.Add(new("Ass.Pivot", "ASS 九宫格定位不能完整保留自定义文字轴心。", SubtitleId: line.Id));
                }
                var px = position.Anchor.X * document.Width + position.Offset.X;
                var py = position.Anchor.Y * document.Height + position.Offset.Y;
                placement += "\\pos(" + AssFormatValues.Number(px) + "," + AssFormatValues.Number(py) + ")";
            }
            placement += "}";
            result.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"Dialogue: {order++},{AssFormatValues.Time(line.Start, MediaTimeRounding.FLOOR)},{AssFormatValues.Time(line.End, MediaTimeRounding.CEILING)},{styles[line.Style with { FontAssetId = null, Position = null }]},,0,0,0,,{placement}{body.Text}"));
        }
        SubtitleFormatLossAnalysis.AddCompositionLoss(document, diagnostics);
        AssFormatValues.CheckText(result.ToString());
        return new(result.ToString(), diagnostics.Distinct().ToImmutableArray());
    }

    private static AssStyleDefinition ParseStyle(Dictionary<string, string> row, double sx, double sy)
    {
        var style = new SubtitleStyle
        {
            FontFamily = Required(row, "Fontname"), FontSize = AssFormatValues.Number(Required(row, "Fontsize")) * sy,
            Fill = AssFormatValues.Color(Required(row, "PrimaryColour")), Stroke = AssFormatValues.Color(Get(row, "OutlineColour", "&H00000000")),
            ShadowColor = AssFormatValues.Color(Get(row, "BackColour", "&H00000000")),
            Bold = AssFormatValues.Integer(Get(row, "Bold", "0")) != 0, Italic = AssFormatValues.Integer(Get(row, "Italic", "0")) != 0,
            Underline = AssFormatValues.Integer(Get(row, "Underline", "0")) != 0, Strikethrough = AssFormatValues.Integer(Get(row, "StrikeOut", "0")) != 0,
            StrokeWidth = AssFormatValues.Number(Get(row, "Outline", "0")) * sy,
            ShadowOffset = new(AssFormatValues.Number(Get(row, "Shadow", "0")) * sx, AssFormatValues.Number(Get(row, "Shadow", "0")) * sy),
            ShadowBlur = 0, Alignment = AssFormatValues.Alignment(AssFormatValues.Integer(Get(row, "Alignment", "2"))),
            Margin = AssFormatValues.Number(Get(row, "MarginV", "0")) * sy
        };
        ProjectValidator.ValidateSubtitleStyle(style);
        style = style with { Position = MarginPosition(style.Alignment,
            AssFormatValues.Number(Get(row, "MarginL", Get(row, "MarginV", "0"))) * sx,
            AssFormatValues.Number(Get(row, "MarginR", Get(row, "MarginV", "0"))) * sx, style.Margin) };
        return new(Required(row, "Name"), style, AssFormatValues.Color(Get(row, "SecondaryColour", "&H000000FF")));
    }

    internal static SubtitlePosition MarginPosition(TextAlignment alignment, double left, double right, double vertical)
    {
        var anchor = AssTextParser.Pivot(alignment);
        return new()
        {
            Anchor = anchor, Pivot = anchor,
            Offset = new(anchor.X.Equals(0d) ? left : anchor.X.Equals(1d) ? -right : 0,
                anchor.Y.Equals(0d) ? vertical : anchor.Y.Equals(1d) ? -vertical : 0)
        };
    }

    private static string[] ParseFormat(string value)
    {
        var format = value.Split(',', StringSplitOptions.TrimEntries);
        if (format.Length == 0 || format.Any(string.IsNullOrEmpty) || format.Distinct(StringComparer.OrdinalIgnoreCase).Count() != format.Length)
        {
            throw new InvalidDataException("ASS Format 含重复或空字段。");
        }
        return format;
    }

    private static Dictionary<string, string> ParseFields(string value, string[] format)
    {
        var values = value.Split(',', format.Length);
        if (values.Length != format.Length)
        {
            throw new InvalidDataException("ASS 字段数量不匹配。");
        }
        return format.Select((field, index) => (field, Value: field.Equals("Text", StringComparison.OrdinalIgnoreCase) ? values[index] : values[index].Trim()))
            .ToDictionary(pair => pair.field, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string Required(Dictionary<string, string> fields, string key) => fields.TryGetValue(key, out var value) ? value : throw new InvalidDataException("ASS 缺少 " + key + " 字段。");
    private static string Get(Dictionary<string, string> fields, string key, string fallback) => fields.TryGetValue(key, out var value) ? value : fallback;
}
