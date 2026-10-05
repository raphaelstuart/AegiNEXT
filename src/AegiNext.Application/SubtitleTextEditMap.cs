using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal sealed class SubtitleTextEditMap
{
    internal SubtitleTextEditMap(string text, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        OldBoundaries = Boundaries(text);
        ValidateRange(text, OldBoundaries, start, length);
        try
        {
            ProjectValidator.ValidateText(replacement);
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("替换文字包含无效 Unicode 或空字符。", nameof(replacement), exception);
        }
        if ((long)text.Length - length + replacement.Length > 1000000)
        {
            throw new ArgumentOutOfRangeException(nameof(replacement), "字幕文字超过预算。");
        }

        Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
        NewBoundaries = Boundaries(Text);
        Delta = replacement.Length - length;
        OriginalStart = start;
        OriginalNewEnd = start + replacement.Length;
        var oldStart = start;
        var oldEnd = start + length;
        while (Array.BinarySearch(NewBoundaries, oldStart) < 0)
        {
            oldStart = OldBoundaries[Array.BinarySearch(OldBoundaries, oldStart) - 1];
        }
        while (Array.BinarySearch(NewBoundaries, oldEnd + Delta) < 0)
        {
            oldEnd = OldBoundaries[Array.BinarySearch(OldBoundaries, oldEnd) + 1];
        }
        OldStart = oldStart;
        OldEnd = oldEnd;
        NewStart = oldStart;
        NewEnd = oldEnd + Delta;
        OldStartIndex = Array.BinarySearch(OldBoundaries, OldStart);
        NewStartIndex = Array.BinarySearch(NewBoundaries, NewStart);
        OldCount = Array.BinarySearch(OldBoundaries, OldEnd) - OldStartIndex;
        NewCount = Array.BinarySearch(NewBoundaries, NewEnd) - NewStartIndex;
    }

    internal string Text { get; }
    internal int[] OldBoundaries { get; }
    internal int[] NewBoundaries { get; }
    internal int Delta { get; }
    internal int OldStart { get; }
    internal int OldEnd { get; }
    internal int NewStart { get; }
    internal int NewEnd { get; }
    internal int OldCount { get; }
    internal int NewCount { get; }
    private int OriginalStart { get; }
    private int OriginalNewEnd { get; }
    private int OldStartIndex { get; }
    private int NewStartIndex { get; }

    internal int MapBoundary(int offset)
    {
        if (offset <= OldStart)
        {
            return offset;
        }
        if (offset >= OldEnd)
        {
            return offset + Delta;
        }
        if (OldCount != NewCount)
        {
            throw new InvalidOperationException("受影响范围内部不能按原索引映射。");
        }
        return NewBoundaries[NewStartIndex + Array.BinarySearch(OldBoundaries, offset) - OldStartIndex];
    }

    internal int StyleSourceOffset(int offset)
    {
        if (offset < NewStart)
        {
            return offset;
        }
        if (offset >= NewEnd)
        {
            return offset - Delta;
        }
        if (OldCount == NewCount)
        {
            return OldBoundaries[OldStartIndex + Array.BinarySearch(NewBoundaries, offset) - NewStartIndex];
        }
        if (offset < OriginalStart)
        {
            return offset;
        }
        if (offset >= OriginalNewEnd)
        {
            return offset - Delta;
        }
        return OriginalStart == OldBoundaries[^1] && OriginalStart > 0 ? OldBoundaries[^2] : OriginalStart;
    }

    internal static int[] Boundaries(string text)
    {
        return StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
    }

    internal static void ValidateRange(string text, int[] boundaries, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > text.Length ||
            Array.BinarySearch(boundaries, start) < 0 || Array.BinarySearch(boundaries, start + length) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "编辑范围必须位于完整字素边界。");
        }
    }
}
