using System.Globalization;

namespace AegiNext.Core.Projects;

/// <summary>按需索引完整 Unicode 字素的 UTF-16 边界；仅保留源文字引用及一份边界数组。</summary>
public sealed class SubtitleTextBoundaries
{
    private int[]? starts;

    /// <summary>创建与源文字实例绑定的字素索引，不立即扫描文字。</summary>
    public SubtitleTextBoundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>此索引对应的源文字实例。</summary>
    public string Text { get; }

    /// <summary>包含文本末尾在内的边界数量；空文字也具有一个边界。</summary>
    public int Length => Starts.Length + 1;

    /// <summary>按位置获取 UTF-16 边界；最后一个边界始终是文本长度。</summary>
    public int this[int index]
    {
        get
        {
            var boundaries = Starts;
            if ((uint)index > (uint)boundaries.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return index == boundaries.Length ? Text.Length : boundaries[index];
        }
    }

    /// <summary>判断偏移是否为完整字素边界；文本起点与终点不需要构建索引。</summary>
    public bool Contains(int offset)
    {
        return offset == 0 || offset == Text.Length ||
            offset > 0 && offset < Text.Length && Array.BinarySearch(Starts, offset) >= 0;
    }

    /// <summary>返回边界位置，未找到时返回插入位置的按位补码，与二分查找一致。</summary>
    public int IndexOf(int offset)
    {
        var boundaries = Starts;
        if (offset == Text.Length)
        {
            return boundaries.Length;
        }
        if (offset > Text.Length)
        {
            return ~(boundaries.Length + 1);
        }
        return Array.BinarySearch(boundaries, offset);
    }

    private int[] Starts => Volatile.Read(ref starts) ?? Initialize();

    private int[] Initialize()
    {
        var boundaries = StringInfo.ParseCombiningCharacters(Text);
        return Interlocked.CompareExchange(ref starts, boundaries, null) ?? boundaries;
    }
}
