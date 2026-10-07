using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;
using AegiNext.Media.Tests.Audio;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证压缩音频在全片低分辨率分析中保留首尾数据和连续 PCM 时间。</summary>
[Collection(nameof(NativeDecoderTestGroup))]
public sealed class AudioAnalysisFullRangeTests
{
    /// <summary>五分钟 AAC 从 44.1 kHz 重采样到 48 kHz 后保持连续时间，直到真实文件末尾。</summary>
    [AudioFact]
    public async Task FiveMinuteAacDecodesThroughItsTailWithoutPcmTimestampRegressionOrEarlyEof()
    {
        using var fixture = await AacFullRangeFixture.CreateAsync();
        using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(WaveformAnalyzer.SAMPLE_RATE, 1));
        decoder.Seek(MediaTime.Zero);
        MediaTime? next = null;
        long frames = 0;
        while (decoder.Read() is { } block)
        {
            Assert.Equal(new AudioSampleFormat(WaveformAnalyzer.SAMPLE_RATE, 1), block.Format);
            if (next is { } expected)
            {
                Assert.Equal(expected, block.Start);
            }
            else
            {
                var firstFrameLimit = new MediaTime(1024, 44100)
                    .ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
                Assert.True(block.Start >= MediaTime.Zero);
                Assert.True(block.Start <= new MediaTime(firstFrameLimit, WaveformAnalyzer.SAMPLE_RATE));
            }
            foreach (var value in block.Samples.Span)
            {
                Assert.True(float.IsFinite(value));
            }
            frames += block.FrameCount;
            next = block.Start + new MediaTime(block.FrameCount, WaveformAnalyzer.SAMPLE_RATE);
        }

        var expectedFrames = fixture.Duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        Assert.InRange(frames, expectedFrames - 4096, expectedFrames + 4096);
        decoder.Seek(fixture.Duration - new MediaTime(1));
        var tail = Assert.IsType<AudioSampleBlock>(decoder.Read());
        Assert.Equal(fixture.Duration - new MediaTime(1), tail.Start);
        Assert.Contains(tail.Samples.ToArray(), value => value > 0.05F);
        Assert.Contains(tail.Samples.ToArray(), value => value < -0.05F);
    }

    /// <summary>局部结果被全片请求替换后，两种层及独立频谱层均覆盖五分钟 AAC 的首尾。</summary>
    [AudioFact]
    public async Task FiveMinuteAacZoomOutReplacesTheOldLocalLayersAndPreservesSpectrumOnlyTailEnergy()
    {
        using var fixture = await AacFullRangeFixture.CreateAsync();
        await using var session = AudioAnalysisSession.Open(fixture.MediaPath, 0, new(MediaTime.Zero), fixture.Duration);
        var initial = await session.GetWindowAsync(new(new(150), 512, 256), true);
        Assert.True(initial.Waveform.Start > MediaTime.Zero);
        const int SAMPLES_PER_BUCKET = 32768;
        var sampleCount = fixture.Duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var request = new WaveformAnalysisRequest(MediaTime.Zero, SAMPLES_PER_BUCKET,
            checked((int)((sampleCount + SAMPLES_PER_BUCKET - 1) / SAMPLES_PER_BUCKET)));

        var full = await session.GetWindowAsync(request, true);

        Assert.Equal(request, full.Waveform.Request);
        Assert.Equal(MediaTime.Zero, full.Waveform.Start);
        Assert.True(full.Waveform.End >= fixture.Duration);
        for (var bucket = 0; bucket < full.Waveform.BucketCount; bucket++)
        {
            Assert.True(full.Waveform.Peaks.Span[bucket * 2] < -0.05F, $"第 {bucket} 个波形桶缺少负峰值。");
            Assert.True(full.Waveform.Peaks.Span[bucket * 2 + 1] > 0.05F, $"第 {bucket} 个波形桶缺少正峰值。");
        }
        var fullSpectrum = Assert.IsType<SpectrogramData>(full.Spectrogram);
        AssertToneCoverage(fullSpectrum, fixture.Duration);
        var spectrumOnly = await session.GetLayersAsync(request, false, true);
        Assert.Null(spectrumOnly.Waveform);
        var spectrum = Assert.IsType<SpectrogramData>(spectrumOnly.Spectrogram);
        AssertToneCoverage(spectrum, fixture.Duration);
        Assert.Equal(fullSpectrum.Start, spectrum.Start);
        Assert.Equal(fullSpectrum.End, spectrum.End);
        Assert.Equal(fullSpectrum.Levels.ToArray(), spectrum.Levels.ToArray());
    }

    /// <summary>AAC 容器声明的相邻帧短于解码帧时，局部缩至全片仍完整返回，PCM 不得重叠。</summary>
    [AudioFact]
    public async Task ShortAacPacketDurationsDoNotTurnAFullRangeRequestIntoOverlappingPcm()
    {
        using var fixture = await AacFullRangeFixture.CreateAsync(1, shortPacketDurations: true, sampleRate: 48000);
        await using var session = AudioAnalysisSession.Open(fixture.MediaPath, 0, new(MediaTime.Zero), fixture.Duration);
        var initial = await session.GetWindowAsync(new(new(1, 2), 512, 16), true);
        Assert.Contains(initial.Waveform.Peaks.ToArray(), value => value > 0.05F);
        var full = await session.GetWindowAsync(new(MediaTime.Zero, 512, 94), true);
        Assert.Equal(MediaTime.Zero, full.Waveform.Start);
        Assert.True(full.Waveform.End >= fixture.Duration);
        Assert.Contains(full.Waveform.Peaks.ToArray(), value => value > 0.05F);
        var spectrum = Assert.IsType<SpectrogramData>(full.Spectrogram);
        var row = (int)(Math.Log(1000.0 / 40) / Math.Log(8000.0 / 40) * spectrum.Height);
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center < new MediaTime(1, 20) || center > new MediaTime(9, 10))
            {
                continue;
            }
            Assert.True(spectrum.Levels.Span[row * spectrum.Width + column] > 100,
                $"第 {column} 列（{center}）缺少压缩帧时长边界后的谱能量。");
        }
    }

    /// <summary>44.1 kHz AAC 的压缩帧时长在 16 kHz 重采样后保持连续，末尾舍入限制在两个样本内。</summary>
    [AudioFact]
    public async Task ShortAacPacketDurationsRemainContinuousWhenResampledToSixteenKilohertz()
    {
        await AssertResampledShortPacketDurationsAsync(16000);
    }

    /// <summary>44.1 kHz AAC 的压缩帧时长在 48 kHz 重采样后保持连续，末尾舍入限制在两个样本内。</summary>
    [AudioFact]
    public async Task ShortAacPacketDurationsRemainContinuousWhenResampledToFortyEightKilohertz()
    {
        await AssertResampledShortPacketDurationsAsync(48000);
    }

    private static async Task AssertResampledShortPacketDurationsAsync(int sampleRate)
    {
        using var fixture = await AacFullRangeFixture.CreateAsync(1, shortPacketDurations: true, sampleRate: 44100);
        using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(sampleRate, 1));
        decoder.Seek(MediaTime.Zero);
        MediaTime? next = null;
        long firstSample = 0;
        long frames = 0;
        double maximumAmplitude = 0;
        while (decoder.Read() is { } block)
        {
            Assert.Equal(new AudioSampleFormat(sampleRate, 1), block.Format);
            if (next is { } expected)
            {
                Assert.Equal(expected, block.Start);
            }
            else
            {
                firstSample = block.Start.ToTimestamp(new(1, sampleRate), MediaTimeRounding.FLOOR).Value;
                var initialFrameLimit = new MediaTime(1024, 44100)
                    .ToTimestamp(new(1, sampleRate), MediaTimeRounding.CEILING).Value;
                Assert.InRange(firstSample, 0, initialFrameLimit);
            }
            frames += block.FrameCount;
            next = block.Start + new MediaTime(block.FrameCount, sampleRate);
            foreach (var value in block.Samples.Span)
            {
                Assert.True(float.IsFinite(value));
                maximumAmplitude = Math.Max(maximumAmplitude, Math.Abs(value));
            }
        }
        var expectedEndSample = fixture.Duration.ToTimestamp(new(1, sampleRate), MediaTimeRounding.TO_EVEN).Value;
        Assert.InRange(firstSample + frames, expectedEndSample - 2, expectedEndSample + 2);
        Assert.True(maximumAmplitude > 0.05);
    }

    private static void AssertToneCoverage(SpectrogramData spectrum, MediaTime duration)
    {
        Assert.True(spectrum.Start <= MediaTime.Zero);
        Assert.True(spectrum.End >= duration);
        var row = (int)(Math.Log(1000.0 / 40) / Math.Log(8000.0 / 40) * spectrum.Height);
        var coveredColumns = 0;
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center < MediaTime.Zero || center >= duration)
            {
                continue;
            }
            Assert.True(spectrum.Levels.Span[row * spectrum.Width + column] > 100,
                $"第 {column} 列（{center}）缺少 1 kHz 谱能量。");
            coveredColumns++;
        }
        Assert.True(coveredColumns > spectrum.Width * 0.99);
    }
}
