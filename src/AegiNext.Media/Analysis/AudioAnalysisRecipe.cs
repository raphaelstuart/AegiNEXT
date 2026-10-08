using System.Security.Cryptography;

namespace AegiNext.Media.Analysis;

/// <summary>决定音频缓存内容的不可变分析参数。</summary>
public sealed record AudioAnalysisRecipe
{
    public int SpectrumSampleRate { get; init; } = 16000;
    public int FftSize { get; init; } = 1024;
    public int HopDivisor { get; init; } = 4;
    public int FrequencyBins { get; init; } = 128;
    public double MinimumFrequency { get; init; } = 40;
    public double MaximumFrequency { get; init; } = 8000;
    public int WaveformBaseSamples { get; init; } = 512;
    public double MinimumDecibels { get; init; } = -80;
    public double MaximumDecibels { get; init; }
    public AudioSpectrumWindow Window { get; init; }
    public int HopSize => FftSize / HopDivisor;
    public int Decimation => WaveformAnalyzer.SAMPLE_RATE / SpectrumSampleRate;
    public int RawPadding => FftSize / 2 * Decimation + AudioSpectrumWindowAnalyzer.FIR_HALF;
    public double WindowMilliseconds => FftSize * 1000.0 / SpectrumSampleRate;
    public double HopMilliseconds => HopSize * 1000.0 / SpectrumSampleRate;
    public double EstimatedCacheBytesPerSecond => 16.0 * WaveformAnalyzer.SAMPLE_RATE / WaveformBaseSamples +
                                                  2.0 * SpectrumSampleRate / HopSize * FrequencyBins;

    /// <summary>验证受支持的 FFT 网格、频率范围、窗函数及强度范围。</summary>
    public void Validate()
    {
        if (SpectrumSampleRate is not (8000 or 16000 or 24000 or 48000) ||
            FftSize is not (512 or 1024 or 2048 or 4096) || HopDivisor is not (2 or 4 or 8) ||
            FrequencyBins is not (64 or 128 or 256 or 512) ||
            WaveformBaseSamples is not (128 or 256 or 512 or 1024 or 2048) || !Enum.IsDefined(Window) ||
            !double.IsFinite(MinimumFrequency) || !double.IsFinite(MaximumFrequency) || MinimumFrequency < 10 ||
            MaximumFrequency > SpectrumSampleRate / 2.0 || MinimumFrequency >= MaximumFrequency ||
            !double.IsFinite(MinimumDecibels) || !double.IsFinite(MaximumDecibels) ||
            MinimumDecibels is < -160 or > -10 || MaximumDecibels is < -40 or > 20 ||
            MaximumDecibels - MinimumDecibels is < 10 or > 160)
        {
            throw new InvalidDataException("音频分析参数无效。");
        }
    }

    internal byte[] GetDigest()
    {
        Validate();
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
        {
            writer.Write(SpectrumSampleRate);
            writer.Write(FftSize);
            writer.Write(HopDivisor);
            writer.Write(FrequencyBins);
            writer.Write(MinimumFrequency);
            writer.Write(MaximumFrequency);
            writer.Write(WaveformBaseSamples);
            writer.Write(MinimumDecibels);
            writer.Write(MaximumDecibels);
            writer.Write((int)Window);
        }
        return SHA256.HashData(memory.ToArray());
    }
}
