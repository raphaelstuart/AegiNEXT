namespace AegiNext.Rendering;

/// <summary>
/// 线性 sRGB、预乘 RGBA F16、左上原点表面；中性 RGB=1 的亮度由参考白指定。
/// </summary>
public sealed record RenderSurfaceInfo
{
    /// <summary>
    /// 显式指定像素尺寸和参考白 nit 标度，并检查完整像素存储的整数范围。
    /// </summary>
    public RenderSurfaceInfo(int width, int height, float referenceWhiteNits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        RenderValidation.Finite(referenceWhiteNits, nameof(referenceWhiteNits));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceWhiteNits);
        Width = width;
        Height = height;
        ReferenceWhiteNits = referenceWhiteNits;
        RowBytes = checked(width * 4 * sizeof(ushort));
        ByteCount = checked(RowBytes * height);
    }

    public int Width { get; }
    public int Height { get; }
    public float ReferenceWhiteNits { get; }
    public int RowBytes { get; }
    public int ByteCount { get; }
    public int ChannelCount => ByteCount / sizeof(ushort);
}
