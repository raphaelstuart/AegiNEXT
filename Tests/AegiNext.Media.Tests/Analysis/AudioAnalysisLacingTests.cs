using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Tests.Audio;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisLacingTests
{
    /// <summary>真实 FLAC lacing 源能够完成波形与频谱窗口分析并保留非零媒体原点。</summary>
    [AudioFact]
    public async Task LacedFlacProducesWaveformAndSpectrumWithANonzeroMediaOrigin()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(offsetMilliseconds: 500);
        await using var session = AudioAnalysisSession.Open(fixture.MediaPath, 0, new(fixture.Origin), fixture.Duration);
        var request = new WaveformAnalysisRequest(new(1, 4), 512, 128);
        var result = await session.GetWindowAsync(request, true);
        Assert.Equal(request.Start, result.Waveform.Start);
        Assert.Contains(result.Waveform.Peaks.ToArray(), sample => sample > 0.4F);
        Assert.NotNull(result.Spectrogram);
        Assert.Contains(result.Spectrogram.Levels.ToArray(), level => level > 150);
        var previous = await session.GetWindowAsync(new(MediaTime.Zero, 512, 64), true);
        Assert.Contains(previous.Spectrogram!.Levels.ToArray(), level => level > 150);
    }

    /// <summary>合法的 Block 间正向缺口在波形和频谱中都保持静默。</summary>
    [AudioFact]
    public async Task ARealTimestampGapRemainsSilentInBothAnalysisLayers()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(shiftMilliseconds: 250);
        await using var session = AudioAnalysisSession.Open(fixture.MediaPath, 0, new(MediaTime.Zero), fixture.Duration);
        var gap = await session.GetWindowAsync(new(new(3, 4), 32, 64), true);
        Assert.All(gap.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(gap.Spectrogram!.Levels.ToArray(), level => Assert.Equal(0, level));
        var after = await session.GetWindowAsync(new(new(5, 4), 512, 32), true);
        Assert.Contains(after.Waveform.Peaks.ToArray(), sample => sample > 0.4F);
        Assert.Contains(after.Spectrogram!.Levels.ToArray(), level => level > 150);
    }

    /// <summary>独立 Block 的真实负向重叠仍由分析 PCM 契约明确拒绝。</summary>
    [AudioFact]
    public async Task ARealTimestampOverlapIsStillRejectedByTheAnalysisPcmReader()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(shiftMilliseconds: -100);
        await using var session = AudioAnalysisSession.Open(fixture.MediaPath, 0, new(MediaTime.Zero), fixture.Duration);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => session.GetWindowAsync(new(MediaTime.Zero, 512, 96), true));
        Assert.Contains("时间戳", error.Message);
    }
}
