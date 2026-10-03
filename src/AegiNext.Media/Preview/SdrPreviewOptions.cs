namespace AegiNext.Media.Preview;

/// <summary>
/// 限制正方形像素的 SDR 显示输出；缩放不会改变原始解码帧。
/// </summary>
public sealed record SdrPreviewOptions
{
    internal const int MAXIMUM_PIXELS = 16_777_216;

    /// <summary>
    /// 指定预览边界；默认适配 1280 × 720，保持裁剪后画面与 SAR 决定的比例。
    /// </summary>
    public SdrPreviewOptions(int maximumWidth = 1280, int maximumHeight = 720)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumWidth, 8192);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumHeight, 8192);
        if ((long)maximumWidth * maximumHeight > MAXIMUM_PIXELS)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth), "预览输出超过像素上限。");
        }

        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
    }

    public int MaximumWidth { get; }

    public int MaximumHeight { get; }
}
