using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>把脚本目标与文字分组解析为原文中的连续字素安全范围，不依赖排版或渲染资源。</summary>
public static class EffectScriptGroupResolver
{
    private const int MAXIMUM_GROUPS = 256;

    /// <summary>在当前范围或显式字幕目标内分组，保留原文字与分隔符；返回顺序由作用域指定，非法边界或预算产生源位置诊断。</summary>
    public static ImmutableArray<EffectScriptTextGroup> Resolve(EffectScriptScope scope, SubtitleLine subtitle,
        AnimationTrackTarget? context = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(subtitle);
        ArgumentNullException.ThrowIfNull(subtitle.Text);
        ArgumentNullException.ThrowIfNull(scope.Target);
        ArgumentNullException.ThrowIfNull(scope.Unit);
        if (!Enum.IsDefined(scope.Order) || !Enum.IsDefined(scope.Unit.Kind))
        {
            throw Error(scope, "文字分组的目标、单位或顺序无效。");
        }
        if (scope.Unit.Kind == EffectScriptUnitKind.CHUNK && scope.Unit.Count <= 0)
        {
            throw Error(scope, "chunk 的字素数量必须大于零。");
        }
        if (scope.Unit.Kind == EffectScriptUnitKind.SPLIT && (scope.Unit.Delimiters.IsDefaultOrEmpty ||
            scope.Unit.Delimiters.Any(delimiter => string.IsNullOrEmpty(delimiter))))
        {
            throw Error(scope, "split 需要至少一个非空文字分隔符。");
        }

        var text = subtitle.Text;
        var boundaries = new SubtitleTextBoundaries(text);
        var (start, end) = ResolveSource(scope, subtitle, context, boundaries);
        var groups = ImmutableArray.CreateBuilder<EffectScriptTextGroup>();
        switch (scope.Unit.Kind)
        {
            case EffectScriptUnitKind.GROUP:
                Add(scope, text, start, end, groups);
                break;
            case EffectScriptUnitKind.GRAPHEME:
                AddGraphemes(scope, text, boundaries, start, end, groups);
                break;
            case EffectScriptUnitKind.CHUNK:
                AddChunks(scope, text, boundaries, start, end, groups);
                break;
            case EffectScriptUnitKind.WORD:
                AddWords(scope, text, boundaries, start, end, groups);
                break;
            case EffectScriptUnitKind.LINE:
                foreach (var line in Lines(text, boundaries, start, end))
                {
                    Add(scope, text, line.Start, line.End, groups);
                }
                break;
            case EffectScriptUnitKind.PARAGRAPH:
                AddParagraphs(scope, text, boundaries, start, end, groups);
                break;
            case EffectScriptUnitKind.SPLIT:
                AddSplit(scope, text, boundaries, start, end, groups);
                break;
            default:
                throw Error(scope, "未知文字分组单位。");
        }

        var result = groups.ToImmutable();
        return scope.Order == EffectScriptOrder.REVERSE ? result.Reverse().ToImmutableArray() : result;
    }

    private static (int Start, int End) ResolveSource(EffectScriptScope scope, SubtitleLine subtitle,
        AnimationTrackTarget? context, SubtitleTextBoundaries boundaries)
    {
        switch (scope.Target.Kind)
        {
            case EffectScriptTargetKind.SUBTITLE:
                return (0, subtitle.Text.Length);
            case EffectScriptTargetKind.CURRENT:
                if (context?.TextRangeId is not { } id)
                {
                    return (0, subtitle.Text.Length);
                }
                var range = subtitle.AnimationRanges.FirstOrDefault(range => range.Id == id);
                if (range is null)
                {
                    throw Error(scope, "当前文字动画范围不属于目标字幕。");
                }
                var rangeEnd = (long)range.Utf16Start + range.Utf16Length;
                if (range.Utf16Start < 0 || range.Utf16Length <= 0 || rangeEnd > subtitle.Text.Length)
                {
                    throw Error(scope, "当前文字动画范围为空或超出目标字幕。");
                }
                if (!boundaries.Contains(range.Utf16Start) || !boundaries.Contains((int)rangeEnd))
                {
                    throw Error(scope, "当前文字动画范围不能拆开完整字素。");
                }
                return (range.Utf16Start, (int)rangeEnd);
            case EffectScriptTargetKind.RANGE:
                var first = (long)scope.Target.Start - 1;
                var last = first + scope.Target.Count;
                if (first < 0 || scope.Target.Count <= 0 || last > boundaries.Length - 1)
                {
                    throw Error(scope, "range 的起点与数量必须是字幕内从 1 开始的完整字素范围。");
                }
                return (boundaries[(int)first], boundaries[(int)last]);
            default:
                throw Error(scope, "未知文字分组目标。");
        }
    }

    private static void AddGraphemes(EffectScriptScope scope, string text, SubtitleTextBoundaries boundaries,
        int start, int end, ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        var last = boundaries.IndexOf(end);
        for (var index = boundaries.IndexOf(start); index < last; index++)
        {
            Add(scope, text, boundaries[index], boundaries[index + 1], groups);
        }
    }

    private static void AddChunks(EffectScriptScope scope, string text, SubtitleTextBoundaries boundaries,
        int start, int end, ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        foreach (var line in Lines(text, boundaries, start, end))
        {
            var index = boundaries.IndexOf(line.Start);
            var last = boundaries.IndexOf(line.End);
            while (index < last)
            {
                var next = index + Math.Min(scope.Unit.Count, last - index);
                Add(scope, text, boundaries[index], boundaries[next], groups);
                index = next;
            }
        }
    }

    private static void AddWords(EffectScriptScope scope, string text, SubtitleTextBoundaries boundaries,
        int start, int end, ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        var wordStart = start;
        var last = boundaries.IndexOf(end);
        for (var index = boundaries.IndexOf(start); index < last; index++)
        {
            var first = boundaries[index];
            var next = boundaries[index + 1];
            if (IsWhitespace(text, first, next))
            {
                Add(scope, text, wordStart, first, groups);
                wordStart = next;
            }
        }
        Add(scope, text, wordStart, end, groups);
    }

    private static void AddParagraphs(EffectScriptScope scope, string text, SubtitleTextBoundaries boundaries,
        int start, int end, ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        var paragraphStart = start;
        var paragraphEnd = start;
        foreach (var line in Lines(text, boundaries, start, end))
        {
            if (IsWhitespace(text, line.Start, line.End))
            {
                Add(scope, text, paragraphStart, paragraphEnd, groups);
                paragraphStart = line.Next;
                paragraphEnd = paragraphStart;
            }
            else
            {
                paragraphEnd = line.End;
            }
        }
        Add(scope, text, paragraphStart, paragraphEnd, groups);
    }

    private static void AddSplit(EffectScriptScope scope, string text, SubtitleTextBoundaries boundaries,
        int start, int end, ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        var delimiters = scope.Unit.Delimiters.OrderByDescending(delimiter => delimiter.Length).ToArray();
        var groupStart = start;
        var cursor = start;
        while (cursor < end)
        {
            string? match = null;
            foreach (var delimiter in delimiters)
            {
                if (delimiter.Length <= end - cursor && text.AsSpan(cursor, delimiter.Length).SequenceEqual(delimiter.AsSpan()))
                {
                    match = delimiter;
                    break;
                }
            }
            if (match is null)
            {
                cursor++;
                continue;
            }
            var next = cursor + match.Length;
            if (!boundaries.Contains(cursor) || !boundaries.Contains(next))
            {
                throw Error(scope, "split 分隔符命中不能拆开组合字、emoji 或 CRLF 的完整字素。");
            }
            AddTrimmed(scope, text, boundaries, groupStart, cursor, groups);
            groupStart = next;
            cursor = next;
        }
        AddTrimmed(scope, text, boundaries, groupStart, end, groups);
    }

    private static void AddTrimmed(EffectScriptScope scope, string text, SubtitleTextBoundaries boundaries,
        int start, int end, ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        var first = boundaries.IndexOf(start);
        var last = boundaries.IndexOf(end);
        while (first < last && IsWhitespace(text, boundaries[first], boundaries[first + 1], horizontalOnly: true))
        {
            first++;
        }
        while (first < last && IsWhitespace(text, boundaries[last - 1], boundaries[last], horizontalOnly: true))
        {
            last--;
        }
        Add(scope, text, boundaries[first], boundaries[last], groups);
    }

    private static IEnumerable<(int Start, int End, int Next)> Lines(string text, SubtitleTextBoundaries boundaries,
        int start, int end)
    {
        var lineStart = start;
        var last = boundaries.IndexOf(end);
        for (var index = boundaries.IndexOf(start); index < last; index++)
        {
            var first = boundaries[index];
            var next = boundaries[index + 1];
            if (text[first] == '\n' || next - first == 2 && text[first] == '\r' && text[first + 1] == '\n')
            {
                yield return (lineStart, first, next);
                lineStart = next;
            }
        }
        yield return (lineStart, end, end);
    }

    private static bool IsWhitespace(string text, int start, int end, bool horizontalOnly = false)
    {
        for (var index = start; index < end; index++)
        {
            var character = text[index];
            if (!char.IsWhiteSpace(character) || horizontalOnly &&
                character != '\t' && char.GetUnicodeCategory(character) != UnicodeCategory.SpaceSeparator)
            {
                return false;
            }
        }
        return true;
    }

    private static void Add(EffectScriptScope scope, string text, int start, int end,
        ImmutableArray<EffectScriptTextGroup>.Builder groups)
    {
        if (start == end || IsWhitespace(text, start, end))
        {
            return;
        }
        if (groups.Count == MAXIMUM_GROUPS)
        {
            throw Error(scope, $"文字分组超过每作用域 {MAXIMUM_GROUPS} 个范围的预算。");
        }
        groups.Add(new(start, end - start));
    }

    private static EffectScriptException Error(EffectScriptScope scope, string message)
    {
        return new(message, scope.Line, scope.Column);
    }
}
