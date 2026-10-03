using AegiNext.Rendering;

namespace AegiNext.Media;

/// <summary>
/// 拥有紧密排列的线性 sRGB 预乘 RGBA Half 数据及显式亮度元数据的不可变上传帧。
/// </summary>
public sealed class HdrFrame
{
    private readonly Half[] pixels;

    /// <summary>
    /// 校验并复制整帧数据；sourcePeakNits 必须覆盖非预乘 RGB 的内容峰值，允许 0.1% 的 F16 量化误差。
    /// </summary>
    public HdrFrame(RenderSurfaceInfo info, ReadOnlySpan<Half> pixels, float sourcePeakNits)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (pixels.Length != info.ChannelCount)
        {
            throw new ArgumentException("像素数量与帧尺寸不一致。", nameof(pixels));
        }

        if (!float.IsFinite(sourcePeakNits) || sourcePeakNits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePeakNits));
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                if (!Half.IsFinite(pixels[i + channel]))
                {
                    throw new ArgumentException("像素分量必须有限。", nameof(pixels));
                }
            }

            var alpha = (float)pixels[i + 3];
            if (alpha is < 0 or > 1)
            {
                throw new ArgumentException("alpha 必须在 [0,1] 范围。", nameof(pixels));
            }

            if (alpha.Equals(0) && (pixels[i] != (Half)0 || pixels[i + 1] != (Half)0 || pixels[i + 2] != (Half)0))
            {
                throw new ArgumentException("透明的预乘像素必须为透明黑。", nameof(pixels));
            }

            for (var channel = 0; channel < 3; channel++)
            {
                if ((double)(float)pixels[i + channel] * info.ReferenceWhiteNits > (double)sourcePeakNits * alpha * 1.001)
                {
                    throw new ArgumentException("声明的内容峰值不能低于帧内的非预乘 RGB 范围。", nameof(sourcePeakNits));
                }
            }
        }

        Info = info;
        SourcePeakNits = sourcePeakNits;
        this.pixels = pixels.ToArray();
    }

    public RenderSurfaceInfo Info { get; }
    public float SourcePeakNits { get; }
    public ReadOnlySpan<Half> Pixels => pixels;
}
