using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

/// <summary>高级 ASS 标签页的临时投影；不保存量化来源，不量化未编辑的工程时间或外观。</summary>
public sealed record AssTextProjection(string Source, ImmutableArray<AssSourceMapEntry> SourceMap,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics)
{
    /// <summary>生成不含位置标签的工程来源与文字映射；时间相对内容原点零，保留 contentOrigin 可见窗口外的旧时间。</summary>
    public static AssTextProjection Create(SubtitleLine line, MediaTime? contentOrigin = null, int canvasWidth = 1920, int canvasHeight = 1080)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        var written = AssTextWriter.Write(line, MediaTime.Zero, true);
        var source = "{\\an" + AssFormatValues.Alignment(line.Style.Alignment).ToString(CultureInfo.InvariantCulture) + "}" + written.Text;
        var parsed = Parser(line).Parse(source);
        return new(source, parsed.SourceMap, written.Diagnostics);
    }

    /// <summary>解析工程来源，保留原行身份、句时间与原生位置；未改写的字时间、字体资源及精确颜色保持原值。</summary>
    public static AssTextEditResult Apply(SubtitleLine original, string source, MediaTime? contentOrigin = null, int canvasWidth = 1920, int canvasHeight = 1080)
    {
        ArgumentNullException.ThrowIfNull(original);
        var projection = Create(original, contentOrigin, canvasWidth, canvasHeight);
        if (source == projection.Source)
        {
            return new(SubtitleKaraokeNormalization.Normalize(original), projection.Diagnostics, projection.SourceMap);
        }
        var baselineResult = Parser(original).Parse(projection.Source);
        var baseline = baselineResult.Line;
        var parsed = Parser(original).Parse(source);
        var line = parsed.Line with
        {
            Style = parsed.Line.Style with
            {
                Alignment = parsed.Line.Style.Alignment == baseline.Style.Alignment ? original.Style.Alignment : parsed.Line.Style.Alignment,
                Position = original.Style.Position
            }
        };
        line = line with { InlineSpans = RestorePrecision(original, baseline, line) };
        var previousIndices = MatchClipSources(projection.Source, baselineResult, source, parsed);
        var clips = line.Karaoke.Select((clip, index) => RestoreClip(original, baseline, parsed.Line, clip, previousIndices[index])).ToImmutableArray();
        line = line with
        {
            Karaoke = original.Karaoke.SequenceEqual(clips) ? original.Karaoke : clips,
            KaraokeStyle = original.KaraokeStyle
        };
        line = SubtitleKaraokeNormalization.Normalize(line);
        AssTextParser.ValidateLine(line);
        return new(line == original ? original : line, parsed.Diagnostics, parsed.SourceMap);
    }

    private static AssTextParser Parser(SubtitleLine line) => new(line,
        new Dictionary<string, AssStyleDefinition>(StringComparer.Ordinal), line.Style.Fill, projectSource: true);

    private static int[] MatchClipSources(string previousSource, AssTextEditResult previous, string source, AssTextEditResult edited)
    {
        var indices = Enumerable.Repeat(-1, edited.Line.Karaoke.Length).ToArray();
        if (previous.KaraokeSourceMap.Length == edited.KaraokeSourceMap.Length)
        {
            for (var index = 0; index < edited.KaraokeSourceMap.Length; index++)
            {
                indices[edited.KaraokeSourceMap[index].SegmentIndex] = previous.KaraokeSourceMap[index].SegmentIndex;
            }
            return indices;
        }
        var tokens = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        foreach (var mapping in previous.KaraokeSourceMap)
        {
            var token = previousSource.Substring(mapping.SourceStart, mapping.SourceLength);
            if (!tokens.TryGetValue(token, out var queue))
            {
                queue = new();
                tokens.Add(token, queue);
            }
            queue.Enqueue(mapping.SegmentIndex);
        }
        var last = -1;
        foreach (var mapping in edited.KaraokeSourceMap)
        {
            var token = source.Substring(mapping.SourceStart, mapping.SourceLength);
            if (!tokens.TryGetValue(token, out var queue))
            {
                continue;
            }
            while (queue.TryPeek(out var candidate) && candidate <= last)
            {
                queue.Dequeue();
            }
            if (queue.TryDequeue(out var matched))
            {
                indices[mapping.SegmentIndex] = matched;
                last = matched;
            }
        }
        return indices;
    }

    private static KaraokeSegment RestoreClip(SubtitleLine original, SubtitleLine baseline, SubtitleLine edited, KaraokeSegment clip, int previousIndex)
    {
        if (previousIndex < 0)
        {
            return clip;
        }
        var previous = original.Karaoke[previousIndex];
        var serialized = baseline.Karaoke[previousIndex];
        var previousStyle = StyleAt(original, previous.Utf16Start);
        var serializedStyle = StyleAt(baseline, serialized.Utf16Start);
        var nextStyle = StyleAt(edited, clip.Utf16Start);
        return clip with
        {
            Id = previous.Id,
            Start = clip.Start == serialized.Start ? previous.Start : clip.Start,
            End = clip.End == serialized.End ? previous.End : clip.End,
            HighlightColor = RestoreColor(previous.HighlightColor, serialized.HighlightColor, clip.HighlightColor),
            ActiveStyle = RestoreVisualOverride(previous.ActiveStyle,
                KaraokeVisualStyleResolver.ResolveActive(previousStyle, original.KaraokeStyle, previous),
                serialized.ActiveStyle?.ApplyTo(serializedStyle) ?? serializedStyle,
                clip.ActiveStyle?.ApplyTo(nextStyle) ?? nextStyle),
            InactiveStyle = RestoreVisualOverride(previous.InactiveStyle,
                KaraokeVisualStyleResolver.ResolveInactive(previousStyle, previous),
                serialized.InactiveStyle?.ApplyTo(serializedStyle) ?? serializedStyle,
                clip.InactiveStyle?.ApplyTo(nextStyle) ?? nextStyle)
        };
    }

    private static KaraokeVisualStyleOverride? RestoreVisualOverride(KaraokeVisualStyleOverride? previous,
        SubtitleStyle native, SubtitleStyle serialized, SubtitleStyle next)
    {
        var restored = (previous ?? new()) with
        {
            Fill = next.Fill == serialized.Fill ? previous?.Fill : RestoreColor(native.Fill, serialized.Fill, next.Fill),
            Stroke = next.Stroke == serialized.Stroke ? previous?.Stroke : RestoreColor(native.Stroke, serialized.Stroke, next.Stroke),
            StrokeWidth = next.StrokeWidth.Equals(serialized.StrokeWidth) ? previous?.StrokeWidth : next.StrokeWidth,
            ShadowColor = next.ShadowColor == serialized.ShadowColor ? previous?.ShadowColor : RestoreColor(native.ShadowColor, serialized.ShadowColor, next.ShadowColor),
            ShadowOffset = next.ShadowOffset == serialized.ShadowOffset ? previous?.ShadowOffset : RestorePoint(native.ShadowOffset, serialized.ShadowOffset, next.ShadowOffset),
            ShadowBlur = next.ShadowBlur.Equals(serialized.ShadowBlur) ? previous?.ShadowBlur : next.ShadowBlur
        };
        return restored.HasOverrides ? restored : null;
    }

    private static SceneColor RestoreColor(SceneColor native, SceneColor serialized, SceneColor next)
    {
        return new(
            next.Red.Equals(serialized.Red) ? native.Red : next.Red,
            next.Green.Equals(serialized.Green) ? native.Green : next.Green,
            next.Blue.Equals(serialized.Blue) ? native.Blue : next.Blue,
            next.Alpha.Equals(serialized.Alpha) ? native.Alpha : next.Alpha);
    }

    private static ScenePoint RestorePoint(ScenePoint native, ScenePoint serialized, ScenePoint next)
    {
        return new(next.X.Equals(serialized.X) ? native.X : next.X, next.Y.Equals(serialized.Y) ? native.Y : next.Y);
    }

    private static ImmutableArray<SubtitleInlineSpan> RestorePrecision(SubtitleLine original, SubtitleLine baseline, SubtitleLine edited)
    {
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        var boundaries = StringInfo.ParseCombiningCharacters(edited.Text).Append(edited.Text.Length).ToArray();
        var textMap = original.Text == edited.Text ? null : MapTextChange(original.Text, edited.Text);
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var offset = boundaries[index];
            var oldOffset = textMap?.StyleSourceOffset(offset) ?? offset;
            var previous = StyleAt(original, oldOffset);
            var previousOverride = OverrideAt(original, oldOffset);
            var serialized = StyleAt(baseline, oldOffset);
            var next = StyleAt(edited, offset);
            var visual = RestoreVisualOverride(previousOverride is null ? null : new()
            {
                Fill = previousOverride.Fill, Stroke = previousOverride.Stroke, StrokeWidth = previousOverride.StrokeWidth,
                ShadowColor = previousOverride.ShadowColor, ShadowOffset = previousOverride.ShadowOffset, ShadowBlur = previousOverride.ShadowBlur
            }, previous, serialized, next);
            var style = (previousOverride ?? new()) with
            {
                Fill = visual?.Fill, Stroke = visual?.Stroke, StrokeWidth = visual?.StrokeWidth,
                ShadowColor = visual?.ShadowColor, ShadowOffset = visual?.ShadowOffset, ShadowBlur = visual?.ShadowBlur,
                FontFamily = next.FontFamily == serialized.FontFamily ? previousOverride?.FontFamily : next.FontFamily,
                FontAssetId = next.FontFamily == serialized.FontFamily ? previousOverride?.FontAssetId : null,
                ClearFontAsset = next.FontFamily == serialized.FontFamily ? previousOverride?.ClearFontAsset ?? false : true,
                FontSize = next.FontSize.Equals(serialized.FontSize) ? previousOverride?.FontSize : next.FontSize,
                Bold = next.Bold == serialized.Bold ? previousOverride?.Bold : next.Bold,
                Italic = next.Italic == serialized.Italic ? previousOverride?.Italic : next.Italic,
                Underline = next.Underline == serialized.Underline ? previousOverride?.Underline : next.Underline,
                Strikethrough = next.Strikethrough == serialized.Strikethrough ? previousOverride?.Strikethrough : next.Strikethrough
            };
            if (!style.HasOverrides)
            {
                continue;
            }
            var length = boundaries[index + 1] - offset;
            if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == offset && spans[^1].Style == style)
            {
                spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
            }
            else
            {
                spans.Add(new(offset, length, style));
            }
        }
        var result = spans.ToImmutable();
        return original.InlineSpans.SequenceEqual(result) ? original.InlineSpans : result;
    }

    private static SubtitleTextEditMap MapTextChange(string original, string edited)
    {
        var previous = SubtitleTextEditMap.Boundaries(original);
        var next = SubtitleTextEditMap.Boundaries(edited);
        var prefix = 0;
        while (prefix < previous.Length - 1 && prefix < next.Length - 1 &&
            original.AsSpan(previous[prefix], previous[prefix + 1] - previous[prefix]).SequenceEqual(edited.AsSpan(next[prefix], next[prefix + 1] - next[prefix])))
        {
            prefix++;
        }
        var previousEnd = previous.Length - 1;
        var nextEnd = next.Length - 1;
        while (previousEnd > prefix && nextEnd > prefix &&
            original.AsSpan(previous[previousEnd - 1], previous[previousEnd] - previous[previousEnd - 1]).SequenceEqual(edited.AsSpan(next[nextEnd - 1], next[nextEnd] - next[nextEnd - 1])))
        {
            previousEnd--;
            nextEnd--;
        }
        return new(original, previous[prefix], previous[previousEnd] - previous[prefix], edited[next[prefix]..next[nextEnd]]);
    }

    private static SubtitleStyle StyleAt(SubtitleLine line, int offset)
    {
        return OverrideAt(line, offset)?.ApplyTo(line.Style) ?? line.Style;
    }

    private static SubtitleInlineStyleOverride? OverrideAt(SubtitleLine line, int offset)
    {
        var start = 0;
        var end = line.InlineSpans.Length - 1;
        while (start <= end)
        {
            var middle = start + (end - start) / 2;
            var span = line.InlineSpans[middle];
            if (offset < span.Utf16Start)
            {
                end = middle - 1;
            }
            else if (offset >= span.Utf16Start + span.Utf16Length)
            {
                start = middle + 1;
            }
            else
            {
                return span.Style;
            }
        }
        return null;
    }
}
