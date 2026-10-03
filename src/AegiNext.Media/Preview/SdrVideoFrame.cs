namespace AegiNext.Media.Preview;

/// <summary>
/// 独立的正方形像素 sRGB、不透明 BGRA8 显示图像；行紧密排列且从上到下。
/// </summary>
public sealed class SdrVideoFrame
{
    /// <summary>
    /// 验证显示布局并复制输入；后续修改输入数组不会影响图像。
    /// </summary>
    public SdrVideoFrame(int width, int height, byte[] pixels) : this(width, height, pixels, false)
    {
    }

    private SdrVideoFrame(int width, int height, byte[] pixels, bool takeOwnership)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height > SdrPreviewOptions.MAXIMUM_PIXELS)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "预览图像超过像素上限。");
        }

        if (pixels.Length != checked(width * height * 4))
        {
            throw new ArgumentException("BGRA8 数据长度与图像尺寸不一致。", nameof(pixels));
        }

        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != byte.MaxValue)
            {
                throw new ArgumentException("SDR 视频预览必须为不透明图像。", nameof(pixels));
            }
        }

        Width = width;
        Height = height;
        Pixels = takeOwnership ? pixels : pixels.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Pixels { get; }

    internal static SdrVideoFrame FromOwnedPixels(int width, int height, byte[] pixels)
    {
        return new(width, height, pixels, true);
    }
}
