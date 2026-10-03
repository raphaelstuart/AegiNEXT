namespace AegiNext.Media.Decoding;

/// <summary>
/// 单个原始像素平面的布局；复制结果仅含按显示行顺序排列的有效字节。
/// </summary>
public sealed record VideoPlaneInfo
{
    /// <summary>
    /// 创建平面布局；原始 stride 可为负，单行平面可为零，行字节数和高度必须为正。
    /// </summary>
    public VideoPlaneInfo(int index, int rowBytes, int height, int sourceStride)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (height > 1 && Math.Abs((long)sourceStride) < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceStride), "原始 stride 不能小于有效行大小。");
        }

        Index = index;
        RowBytes = rowBytes;
        Height = height;
        SourceStride = sourceStride;
    }

    public int Index { get; }

    public int RowBytes { get; }

    public int Height { get; }

    public int SourceStride { get; }

    public int ByteCount => checked(RowBytes * Height);
}
