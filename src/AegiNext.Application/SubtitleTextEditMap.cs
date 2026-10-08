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
        Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
        NewBoundaries = Boundaries(Text);
        Delta = replacement.Length - length;
        OriginalStart = start;
        OriginalNewEnd = start + replacement.Length;
        var oldStart = start;
        var oldEnd = start + length;
        while (NewBoundaries.IndexOf(oldStart) < 0)
        {
            oldStart = OldBoundaries[OldBoundaries.IndexOf(oldStart) - 1];
        }
        while (NewBoundaries.IndexOf(oldEnd + Delta) < 0)
        {
            oldEnd = OldBoundaries[OldBoundaries.IndexOf(oldEnd) + 1];
        }
        OldStart = oldStart;
        OldEnd = oldEnd;
        NewStart = oldStart;
        NewEnd = oldEnd + Delta;
        OldStartIndex = OldBoundaries.IndexOf(OldStart);
        NewStartIndex = NewBoundaries.IndexOf(NewStart);
        OldCount = OldBoundaries.IndexOf(OldEnd) - OldStartIndex;
        NewCount = NewBoundaries.IndexOf(NewEnd) - NewStartIndex;
    }

    internal string Text { get; }
    internal SubtitleTextBoundaries OldBoundaries { get; }
    internal SubtitleTextBoundaries NewBoundaries { get; }
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
        return NewBoundaries[NewStartIndex + OldBoundaries.IndexOf(offset) - OldStartIndex];
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
            return OldBoundaries[OldStartIndex + NewBoundaries.IndexOf(offset) - NewStartIndex];
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

    internal static SubtitleTextBoundaries Boundaries(string text)
    {
        return new(text);
    }

    internal static void ValidateRange(string text, SubtitleTextBoundaries boundaries, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > text.Length ||
            !boundaries.Contains(start) || !boundaries.Contains(start + length))
        {
            throw new ArgumentOutOfRangeException(nameof(start), "编辑范围必须位于完整字素边界。");
        }
    }
}
