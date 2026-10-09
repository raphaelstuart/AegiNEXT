namespace AegiNext.Rendering.Fonts;

/// <summary>独立于主题的纯托管字体预览；Alpha 是紧密排列的单通道八位字形覆盖率。</summary>
public sealed class FontNamePreview
{
    /// <summary>建立物理像素尺寸与 DIP 呈现尺寸明确的字形快照，并复制调用方的覆盖率数据。</summary>
    public FontNamePreview(int pixelWidth, int pixelHeight, double logicalWidth, double logicalHeight,
        ReadOnlyMemory<byte> alpha)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        if (!double.IsFinite(logicalWidth) || logicalWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        }

        if (!double.IsFinite(logicalHeight) || logicalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalHeight));
        }

        if (alpha.Length != checked(pixelWidth * pixelHeight))
        {
            throw new ArgumentException("字形覆盖率长度必须匹配预览像素尺寸。", nameof(alpha));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        Alpha = alpha.ToArray();
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double LogicalWidth { get; }
    public double LogicalHeight { get; }
    public ReadOnlyMemory<byte> Alpha { get; }
}
