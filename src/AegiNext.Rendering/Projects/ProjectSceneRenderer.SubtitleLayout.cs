using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private const int WRAP_PROBE_GRAPHEMES = 32;
    /// <summary>返回与实际渲染共用的不可变 run、字素、选区和光标几何；快照不持有原生资源。</summary>
    public SubtitleTextLayout MeasureSubtitleTextLayout(ProjectDocument document, SubtitleLine subtitle)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subtitle);
        Prepare(document);
        return Layout(document, subtitle).Snapshot;
    }

    private SubtitleLayout Layout(ProjectDocument document, SubtitleLine subtitle)
    {
        var key = (subtitle, document.Width, document.Height);
        if (layouts.TryGetValue(key, out var existing))
        {
            return existing;
        }
        if (layouts.Count >= 256)
        {
            ClearLayouts();
        }
        var lines = new List<SubtitleLayoutLine>();
        var offset = 0;
        try
        {
            foreach (var paragraph in subtitle.Text.Split('\n'))
            {
                var text = paragraph.TrimEnd('\r');
                if (text.Length == 0)
                {
                    lines.Add(new(string.Empty, offset, [], (float)subtitle.Style.FontSize, 0));
                    offset += paragraph.Length + 1;
                    continue;
                }
                var direction = text.EnumerateRunes().Any(value => value.Value is >= 0x0590 and <= 0x08ff)
                    ? TextDirection.RIGHT_TO_LEFT : TextDirection.LEFT_TO_RIGHT;
                var boundaries = StringInfo.ParseCombiningCharacters(text);
                var begin = 0;
                while (begin < text.Length)
                {
                    var runs = ShapeWrappedRuns(document, subtitle, text, offset, begin, boundaries, direction, out var end);
                    lines.Add(new(text[begin..end], offset + begin, runs, (float)runs.Max(run => run.Style.FontSize),
                        runs.Sum(run => run.Shape.AdvanceWidth)));
                    begin = end;
                }
                offset += paragraph.Length + 1;
            }
            var result = PositionLayout(document, subtitle, lines);
            layouts.Add(key, result);
            return result;
        }
        catch
        {
            foreach (var line in lines)
            {
                DisposeRuns(line.Runs);
            }
            throw;
        }
    }

    private ImmutableArray<SubtitleLayoutRun> ShapeWrappedRuns(ProjectDocument document, SubtitleLine subtitle,
        string text, int offset, int begin, int[] boundaries, TextDirection direction, out int end)
    {
        var startBoundary = Array.BinarySearch(boundaries, begin);
        var available = Math.Max(1, document.Width - subtitle.Style.Margin * 2);
        var low = startBoundary + 1;
        var high = Math.Min(boundaries.Length, startBoundary + WRAP_PROBE_GRAPHEMES);
        while (true)
        {
            var candidateEnd = high == boundaries.Length ? text.Length : boundaries[high];
            var candidate = ShapeRuns(document, subtitle, text[begin..candidateEnd], offset + begin, direction);
            if (candidate.Sum(run => run.Shape.AdvanceWidth) <= available)
            {
                if (high == boundaries.Length)
                {
                    end = candidateEnd;
                    return candidate;
                }
                low = high;
                high = Math.Min(boundaries.Length, startBoundary + (high - startBoundary) * 2);
                DisposeRuns(candidate);
                continue;
            }
            DisposeRuns(candidate);
            high--;
            break;
        }
        high = Math.Max(low, high);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var candidateEnd = middle == boundaries.Length ? text.Length : boundaries[middle];
            var candidate = ShapeRuns(document, subtitle, text[begin..candidateEnd], offset + begin, direction);
            var width = candidate.Sum(run => run.Shape.AdvanceWidth);
            DisposeRuns(candidate);
            if (width <= available)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        end = low == boundaries.Length ? text.Length : boundaries[low];
        return ShapeRuns(document, subtitle, text[begin..end], offset + begin, direction);
    }

    private ImmutableArray<SubtitleLayoutRun> ShapeRuns(ProjectDocument document, SubtitleLine subtitle,
        string text, int offset, TextDirection direction)
    {
        var runs = ImmutableArray.CreateBuilder<SubtitleLayoutRun>();
        try
        {
            var start = 0;
            while (start < text.Length)
            {
                var style = subtitle.Style;
                var end = text.Length;
                var spanIndex = FindInlineSpan(subtitle.InlineSpans, offset + start);
                if (spanIndex < subtitle.InlineSpans.Length)
                {
                    var span = subtitle.InlineSpans[spanIndex];
                    if (span.Utf16Start <= offset + start)
                    {
                        style = span.Style.ApplyTo(style);
                        end = Math.Min(end, span.Utf16Start + span.Utf16Length - offset);
                    }
                    else
                    {
                        end = Math.Min(end, span.Utf16Start - offset);
                    }
                }
                var piece = text[start..end];
                ShapeFontRuns(document, style, piece, offset + start, direction, runs);
                start = end;
            }
            return runs.ToImmutable();
        }
        catch
        {
            DisposeRuns(runs);
            throw;
        }
    }

    private static int FindInlineSpan(ImmutableArray<SubtitleInlineSpan> spans, int offset)
    {
        var low = 0;
        var high = spans.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (spans[middle].Utf16Start + spans[middle].Utf16Length <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }

    private static void DisposeRuns(IEnumerable<SubtitleLayoutRun> runs)
    {
        foreach (var run in runs)
        {
            run.Shape.Dispose();
        }
    }

    private static SubtitleLayout PositionLayout(ProjectDocument document, SubtitleLine subtitle,
        List<SubtitleLayoutLine> lines)
    {
        var style = subtitle.Style;
        var horizontal = (int)style.Alignment % 3;
        var vertical = (int)style.Alignment / 3;
        var blockHeight = lines[^1].FontSize;
        for (var index = 0; index < lines.Count - 1; index++)
        {
            blockHeight += lines[index].FontSize * (float)style.LineHeight;
        }
        var top = vertical switch
        {
            0 => (float)style.Margin,
            1 => (document.Height - blockHeight) / 2,
            _ => document.Height - (float)style.Margin - blockHeight
        };
        var ink = SKRect.Empty;
        var rowTop = top;
        var geometries = ImmutableArray.CreateBuilder<SubtitleTextRunGeometry>();
        var graphemes = ImmutableArray.CreateBuilder<SubtitleGraphemeGeometry>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var localInk = SKRect.Empty;
            var positioned = ImmutableArray.CreateBuilder<SubtitleLayoutRun>();
            var advance = 0f;
            var rtl = !line.Runs.IsEmpty && line.Runs[0].Direction == TextDirection.RIGHT_TO_LEFT;
            foreach (var run in line.Runs)
            {
                var runX = rtl ? line.AdvanceWidth - advance - run.Shape.AdvanceWidth : advance;
                var bounds = RunInkBounds(run);
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(runX, 0);
                    localInk = localInk.IsEmpty ? bounds : SKRect.Union(localInk, bounds);
                }
                positioned.Add(run with { Position = new(runX, 0) });
                advance += run.Shape.AdvanceWidth;
            }
            var left = localInk.IsEmpty ? 0 : localInk.Left;
            var width = localInk.IsEmpty ? line.AdvanceWidth : localInk.Width;
            var x = horizontal switch
            {
                0 => (float)style.Margin - left,
                1 => (document.Width - width) / 2 - left,
                _ => document.Width - (float)style.Margin - width - left
            };
            var baseline = new SKPoint(x, rowTop + line.FontSize);
            for (var runIndex = 0; runIndex < positioned.Count; runIndex++)
            {
                var run = positioned[runIndex];
                var position = new SKPoint(baseline.X + run.Position.X, baseline.Y);
                var runGraphemes = MeasureGraphemes(run, position, index, rowTop,
                    rowTop + line.FontSize * (float)style.LineHeight);
                positioned[runIndex] = run with { Position = position, Graphemes = runGraphemes };
                var bounds = RunInkBounds(run);
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(position);
                }
                geometries.Add(new(run.Utf16Offset, run.Text.Length, index, run.Style, position, bounds)
                {
                    ResolvedFontFamily = run.ResolvedFontFamily,
                    ResolvedFontVariant = run.ResolvedFontVariant
                });
                graphemes.AddRange(runGraphemes);
                if (!bounds.IsEmpty)
                {
                    ink = ink.IsEmpty ? bounds : SKRect.Union(ink, bounds);
                }
            }
            lines[index] = line with { Position = baseline, Runs = positioned.ToImmutable() };
            rowTop += line.FontSize * (float)style.LineHeight;
        }
        AddNewlineGeometry(subtitle.Text, lines, graphemes, style.LineHeight);
        var orderedGraphemes = graphemes.OrderBy(value => value.Utf16Start).ToImmutableArray();
        PopulateKaraoke(subtitle, lines, orderedGraphemes);
        var hasInk = !ink.IsEmpty;
        if (!hasInk)
        {
            var width = Math.Max(1, lines.Max(line => line.AdvanceWidth));
            var x = horizontal switch
            {
                0 => (float)style.Margin,
                1 => (document.Width - width) / 2,
                _ => document.Width - (float)style.Margin - width
            };
            ink = new(x, top, x + width, top + blockHeight);
        }
        var normalized = style.Position ?? SubtitlePosition.FromAlignment(style.Alignment, style.Margin);
        var pivot = new SKPoint(ink.Left + (float)normalized.Pivot.X * ink.Width,
            ink.Top + (float)normalized.Pivot.Y * ink.Height);
        var basePosition = style.Position is not null
            ? new SKPoint((float)(normalized.Anchor.X * document.Width + normalized.Offset.X),
                (float)(normalized.Anchor.Y * document.Height + normalized.Offset.Y))
            : new SKPoint((float)(normalized.Anchor.X * document.Width + normalized.Offset.X), pivot.Y);
        var first = lines[0];
        var emptyCaret = new SKRect(first.Position.X, first.Position.Y - first.FontSize,
            first.Position.X + 1, first.Position.Y + first.FontSize * ((float)style.LineHeight - 1));
        var snapshot = new SubtitleTextLayout(subtitle.Text, ink, basePosition, pivot, hasInk,
            geometries.ToImmutable(), orderedGraphemes, emptyCaret);
        return new(lines, ink, basePosition, pivot, hasInk, snapshot);
    }

    private static ImmutableArray<SubtitleGraphemeGeometry> MeasureGraphemes(SubtitleLayoutRun run, SKPoint position,
        int lineIndex, float top, float bottom)
    {
        var starts = StringInfo.ParseCombiningCharacters(run.Text);
        var clusters = run.Shape.Glyphs.ToArray().GroupBy(value => value.Utf16Cluster)
            .ToDictionary(group => group.Key, group => group.Min(value => value.Position.X));
        var keys = clusters.Keys.Order().ToArray();
        var physical = clusters.Values.Distinct().Order().ToArray();
        var result = ImmutableArray.CreateBuilder<SubtitleGraphemeGeometry>();
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var keyIndex = Array.BinarySearch(keys, start);
            if (keyIndex < 0)
            {
                keyIndex = Math.Max(0, ~keyIndex - 1);
            }
            var clusterStart = keys[keyIndex];
            var clusterEnd = keyIndex + 1 < keys.Length ? keys[keyIndex + 1] : run.Text.Length;
            var firstMember = BoundaryIndex(starts, clusterStart);
            var memberCount = Math.Max(1, BoundaryIndex(starts, clusterEnd) - firstMember);
            var member = index - firstMember;
            var left = clusters[clusterStart];
            var physicalIndex = Array.BinarySearch(physical, left);
            var right = physicalIndex + 1 < physical.Length ? physical[physicalIndex + 1] : run.Shape.AdvanceWidth;
            var step = (right - left) / memberCount;
            var rtl = run.Direction == TextDirection.RIGHT_TO_LEFT;
            var leading = position.X + left + step * (rtl ? memberCount - member : member);
            var trailing = leading + (rtl ? -step : step);
            var length = (index + 1 < starts.Length ? starts[index + 1] : run.Text.Length) - start;
            result.Add(new(run.Utf16Offset + start, length, lineIndex,
                new(Math.Min(leading, trailing), top, Math.Max(leading, trailing), bottom),
                new(leading, top, leading + 1, bottom), new(trailing, top, trailing + 1, bottom)));
        }
        return result.ToImmutable();
    }

    private static void AddNewlineGeometry(string text, List<SubtitleLayoutLine> lines,
        ImmutableArray<SubtitleGraphemeGeometry>.Builder graphemes, double lineHeight)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var length = (index + 1 < starts.Length ? starts[index + 1] : text.Length) - start;
            if (text[start] is not ('\r' or '\n'))
            {
                continue;
            }
            var lineIndex = 0;
            while (lineIndex + 1 < lines.Count && lines[lineIndex + 1].Utf16Offset <= start)
            {
                lineIndex++;
            }
            var line = lines[lineIndex];
            var next = lines[Math.Min(lineIndex + 1, lines.Count - 1)];
            var leading = line.Runs.IsEmpty ? line.Position.X : line.Runs[^1].Graphemes[^1].TrailingCaret.Left;
            var top = line.Position.Y - line.FontSize;
            var bottom = top + line.FontSize * (float)lineHeight;
            var nextTop = next.Position.Y - next.FontSize;
            graphemes.Add(new(start, length, lineIndex, new(leading, top, leading, bottom),
                new(leading, top, leading + 1, bottom),
                new(next.Position.X, nextTop, next.Position.X + 1, nextTop + next.FontSize * (float)lineHeight)));
        }
    }

    private static int BoundaryIndex(int[] boundaries, int value)
    {
        var index = Array.BinarySearch(boundaries, value);
        return index < 0 ? ~index : index;
    }

    private static void PopulateKaraoke(SubtitleLine subtitle, List<SubtitleLayoutLine> lines,
        ImmutableArray<SubtitleGraphemeGeometry> graphemes)
    {
        if (subtitle.Karaoke.IsEmpty)
        {
            return;
        }
        var starts = graphemes.Select(glyph => glyph.Utf16Start).ToArray();
        var cumulative = new double[graphemes.Length + 1];
        for (var index = 0; index < graphemes.Length; index++)
        {
            cumulative[index + 1] = cumulative[index] + graphemes[index].Bounds.Width;
        }
        var totals = subtitle.Karaoke.Select(segment => (float)(cumulative[BoundaryIndex(starts, segment.Utf16Start + segment.Utf16Length)] -
            cumulative[BoundaryIndex(starts, segment.Utf16Start)])).ToArray();
        var advances = new float[subtitle.Karaoke.Length];
        var segmentIndex = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var runs = ImmutableArray.CreateBuilder<SubtitleLayoutRun>();
            foreach (var run in line.Runs)
            {
                while (segmentIndex < subtitle.Karaoke.Length && subtitle.Karaoke[segmentIndex].Utf16Start +
                    subtitle.Karaoke[segmentIndex].Utf16Length <= run.Utf16Offset)
                {
                    segmentIndex++;
                }
                var spans = ImmutableArray.CreateBuilder<SubtitleKaraokeSpan>();
                var runStarts = run.Graphemes.Select(glyph => glyph.Utf16Start).ToArray();
                for (var current = segmentIndex; current < subtitle.Karaoke.Length; current++)
                {
                    var segment = subtitle.Karaoke[current];
                    if (segment.Utf16Start >= run.Utf16Offset + run.Text.Length)
                    {
                        break;
                    }
                    var first = BoundaryIndex(runStarts, segment.Utf16Start);
                    var end = BoundaryIndex(runStarts, segment.Utf16Start + segment.Utf16Length);
                    if (first == end)
                    {
                        continue;
                    }
                    var bounds = run.Graphemes[first].Bounds;
                    var width = 0f;
                    for (var glyph = first; glyph < end; glyph++)
                    {
                        bounds = SKRect.Union(bounds, run.Graphemes[glyph].Bounds);
                        width += run.Graphemes[glyph].Bounds.Width;
                    }
                    spans.Add(new(segment, bounds, advances[current], totals[current],
                        run.Direction == TextDirection.RIGHT_TO_LEFT, first == 0, end == run.Graphemes.Length));
                    advances[current] += width;
                }
                runs.Add(run with { Karaoke = spans.ToImmutable() });
            }
            lines[index] = line with { Runs = runs.ToImmutable() };
        }
    }

    private static SKRect RunInkBounds(SubtitleLayoutRun run)
    {
        var bounds = run.Shape.InkBounds;
        foreach (var decoration in Decorations(run))
        {
            bounds = bounds.IsEmpty ? decoration : SKRect.Union(bounds, decoration);
        }
        return bounds;
    }

    private static IEnumerable<SKRect> Decorations(SubtitleLayoutRun run)
    {
        var metrics = run.Shape.FontMetrics;
        if (run.Style.Underline)
        {
            var position = metrics.UnderlinePosition ?? (float)run.Style.FontSize * 0.1f;
            var thickness = Math.Max(1, metrics.UnderlineThickness ?? (float)run.Style.FontSize * 0.05f);
            yield return new(0, position, run.Shape.AdvanceWidth, position + thickness);
        }
        if (run.Style.Strikethrough)
        {
            var position = metrics.StrikeoutPosition ?? -(float)run.Style.FontSize * 0.3f;
            var thickness = Math.Max(1, metrics.StrikeoutThickness ?? (float)run.Style.FontSize * 0.05f);
            yield return new(0, position, run.Shape.AdvanceWidth, position + thickness);
        }
    }
}
