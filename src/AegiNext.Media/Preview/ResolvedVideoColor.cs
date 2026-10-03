using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>
/// 当前预览可以明确解释的帧级色彩；不推测缺失字段，也不改写原始事实。
/// </summary>
public sealed record ResolvedVideoColor
{
    private ResolvedVideoColor(VideoFrameInfo frame)
    {
        Range = frame.ColorRangeCode;
        Matrix = frame.ColorMatrixCode;
        Primaries = frame.ColorPrimariesCode;
        Transfer = frame.ColorTransferCode;
        ChromaLocation = frame.ChromaLocationCode;
        AlphaMode = frame.AlphaModeCode;
    }

    public int Range { get; }

    public int Matrix { get; }

    public int Primaries { get; }

    public int Transfer { get; }

    public int ChromaLocation { get; }

    public int AlphaMode { get; }

    /// <summary>
    /// 验证首版逐行、不透明整数输入；未知或需要专用解释的组合明确拒绝。
    /// </summary>
    public static ResolvedVideoColor Resolve(VideoFrameInfo frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.IsCorrupt || frame.DecodeErrorFlags != 0)
        {
            throw new InvalidDataException("损坏视频帧不能用于预览。");
        }

        if (frame.IsInterlaced)
        {
            throw new NotSupportedException("当前预览尚未支持隔行视频。");
        }

        foreach (var name in frame.SideDataTypes)
        {
            if (name.Contains("dynamic", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("dovi", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("dolby", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("display matrix", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("stereo", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("icc", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("raw color", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("film grain", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ambient", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"当前预览尚未支持帧信息：{name}。");
            }
        }

        if (frame.ComponentDepths.Length != 3 || frame.ComponentDepths.Any(depth => depth is < 8 or > 16))
        {
            throw new NotSupportedException($"当前预览需要 8 至 16 位、不透明的三分量整数图像：{frame.PixelFormat}。");
        }

        if (frame.ColorPrimariesCode is not (1 or 9) || frame.ColorTransferCode is not (1 or 13 or 16 or 18) ||
            frame.ColorRangeCode is not (1 or 2))
        {
            throw new NotSupportedException($"预览需要明确的 BT.709／BT.2020 基色、BT.709／sRGB／PQ／HLG 传递函数及范围。当前：{frame.Color}。");
        }

        var format = frame.PixelFormat;
        var isRgb = format.StartsWith("rgb", StringComparison.Ordinal) ||
            format.StartsWith("bgr", StringComparison.Ordinal) || format.StartsWith("gbr", StringComparison.Ordinal);
        var isYuv = format.StartsWith("yuv", StringComparison.Ordinal) ||
            format is "nv12" or "nv21" || format.StartsWith("p010", StringComparison.Ordinal) ||
            format.StartsWith("p016", StringComparison.Ordinal);
        if ((!isRgb && !isYuv) ||
            (isRgb && (frame.ColorMatrixCode != 0 || frame.ColorRangeCode != 2)) ||
            (format.StartsWith("yuvj", StringComparison.Ordinal) && frame.ColorRangeCode != 2) ||
            (isYuv && frame.ColorMatrixCode is not (1 or 5 or 6 or 9)))
        {
            throw new NotSupportedException($"预览不支持或无法明确解释像素格式／矩阵／范围：{format} / {frame.ColorMatrixCode} / {frame.ColorRangeCode}。");
        }

        var isSubsampled = isYuv && (format.Contains("420", StringComparison.Ordinal) ||
            format.Contains("422", StringComparison.Ordinal) || format.Contains("440", StringComparison.Ordinal) ||
            format.Contains("411", StringComparison.Ordinal) || format.Contains("410", StringComparison.Ordinal) ||
            format.StartsWith("nv", StringComparison.Ordinal) || format.StartsWith("p0", StringComparison.Ordinal));
        if (frame.ChromaLocationCode is < 0 or > 6 || (isSubsampled && frame.ChromaLocationCode == 0))
        {
            throw new NotSupportedException("子采样视频预览需要明确的色度采样位置。");
        }

        return new(frame);
    }
}
