namespace AegiNext.Media.Analysis;

/// <summary>即时更新的波形增益和语谱显示映射，不改变分析缓存。</summary>
public sealed record AudioAnalysisDisplayOptions
{
    public double WaveformGain { get; init; } = 1;
    public double SpectrumBrightness { get; init; } = 1;
    public double SpectrumContrast { get; init; } = 1;

    /// <summary>验证有限且受支持的显示系数。</summary>
    public void Validate()
    {
        if (!double.IsFinite(WaveformGain) || WaveformGain is < 0.25 or > 8 ||
            !double.IsFinite(SpectrumBrightness) || SpectrumBrightness is < 0.25 or > 4 ||
            !double.IsFinite(SpectrumContrast) || SpectrumContrast is < 0.25 or > 4)
        {
            throw new InvalidDataException("音频分析显示参数无效。");
        }
    }
}
