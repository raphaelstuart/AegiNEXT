using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证完整缓存和独立极细波形读取的生命周期及媒体时间网格。</summary>
public sealed class AudioAnalysisSessionTests
{
    /// <summary>阻塞的完整扫描不阻塞独立细节源，视口替换只取消概览等待。</summary>
    [Fact]
    public async Task BlockedCacheScanDoesNotPreventIndependentFineWaveform()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 4L)
        {
            BeforeRead = token =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    entered.TrySetResult();
                    release.Wait(token);
                }
            }
        };
        var detailSource = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 4L);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(4),
            detailSourceFactory: _ => detailSource);
        var overview = session.GetOverviewAsync(new(MediaTime.Zero, 2048, 94), false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var result = await session.GetWindowAsync(new(new(2), 32, 100), false).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new MediaTime(2), result.Waveform.Start);
            Assert.Equal(0.5F, result.Waveform.Peaks.Span[1]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => overview.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(session.IsCacheComplete);
            Assert.Equal(0, source.CancelCount);
            Assert.Single(source.SeekTargets);
            Assert.Equal(MediaTime.Zero, source.SeekTargets[0]);
            Assert.True(detailSource.SeekTargets[0] > new MediaTime(1));
        }
        finally
        {
            release.Set();
        }
        await session.PrepareCacheAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, detailSource.DisposeCount);
    }

    /// <summary>关闭会取消阻塞的构建源，等待读取和解码器释放后再清理缓存。</summary>
    [Fact]
    public async Task ClosingCancelsABlockedCacheScanAndWaitsForDecoderDisposal()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 4L)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(4));
        var pending = session.GetWindowAsync(new(MediaTime.Zero, 512, 100), true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, session.CachedBytes);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = session.GetWindowAsync(new(MediaTime.Zero, 32, 100), false);
        });
    }

    /// <summary>缓存和独立细节解码均保留 PTS 缺口及 EOF 补零，不提前后续样本。</summary>
    [Fact]
    public async Task PtsGapsAndEndPaddingRemainSilentRatherThanMovingLaterSamplesEarlier()
    {
        static WindowAudioSource Source() => new(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 3L, 317)
        {
            Gap = (WaveformAnalyzer.SAMPLE_RATE, WaveformAnalyzer.SAMPLE_RATE * 2L)
        };
        var source = Source();
        var detailSource = Source();
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(5),
            detailSourceFactory: _ => detailSource);
        await session.PrepareCacheAsync();
        var gap = await session.GetWindowAsync(new(new(5, 4), 32, 64), true);
        Assert.All(gap.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(gap.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
        var later = await session.GetWindowAsync(new(new(9, 4), 32, 64), false);
        Assert.Equal(0.5F, later.Waveform.Peaks.Span[1]);
        var padded = await session.GetWindowAsync(new(new(4), 32, 64), true);
        Assert.All(padded.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(padded.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
        Assert.Equal(1, source.SeekCount);
    }

    /// <summary>长媒体极细窗口只读取局部 PCM，并精确保留单样本脉冲所在采样桶。</summary>
    [Theory]
    [InlineData(600)]
    [InlineData(3600)]
    public async Task LongMediaKeepsTheImpulseInsideItsExactLocalSampleBucket(int duration)
    {
        const long IMPULSE_SAMPLE = 2885904;
        var source = new WindowAudioSource(index => index == IMPULSE_SAMPLE ? 0.8F : 0F,
            (long)duration * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => throw new InvalidOperationException("不得启动完整扫描。"),
            new(MediaTime.Zero), new(duration), detailSourceFactory: _ => source);
        var request = new WaveformAnalysisRequest(new(59), 32, 3000);
        var window = await session.GetWindowAsync(request, false);
        var waveform = window.Waveform;
        var startSample = waveform.Start.ToTimestamp(new(1, waveform.PcmSampleRate), MediaTimeRounding.FLOOR).Value;
        var bucket = (int)((IMPULSE_SAMPLE - startSample) / waveform.SamplesPerBucket);

        Assert.Equal(0.8F, waveform.Peaks.Span[bucket * 2 + 1]);
        var impulseTime = new MediaTime(IMPULSE_SAMPLE, waveform.PcmSampleRate);
        var bucketStart = waveform.Start + new MediaTime((long)bucket * waveform.SamplesPerBucket, waveform.PcmSampleRate);
        Assert.True(bucketStart <= impulseTime && impulseTime < bucketStart + new MediaTime(32, waveform.PcmSampleRate));
        Assert.Equal(request, waveform.Request);
        Assert.Null(window.Spectrogram);
        Assert.InRange(session.CachedBytes, 1, 16L * 1024 * 1024);
        Assert.InRange(source.FramesRead, 1, 3L * 65536);
        Assert.False(session.IsCacheComplete);
    }

    /// <summary>极细 PCM 页可复用和淘汰，已交付波形不依赖仍驻留的缓存页。</summary>
    [Fact]
    public async Task AdjacentDetailRequestsReuseTilesAndEvictionDoesNotInvalidateDeliveredData()
    {
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 5L);
        await using var session = new AudioAnalysisSession(_ => throw new InvalidOperationException("不得启动完整扫描。"),
            new(MediaTime.Zero), new(5), maximumCachedBytes: 8192, detailSourceFactory: _ => source);
        var first = await session.GetWindowAsync(new(MediaTime.Zero, 1, 16), false);
        var seeks = source.SeekCount;
        await session.GetWindowAsync(new(new(8, WaveformAnalyzer.SAMPLE_RATE), 1, 16), false);
        Assert.Equal(seeks, source.SeekCount);
        await session.GetWindowAsync(new(new(1), 1, 16), false);
        Assert.True(source.SeekCount > seeks);
        Assert.InRange(session.CachedBytes, 1, 8192);
        Assert.Equal(0.5F, first.Waveform.Peaks.Span[1]);
        var afterEviction = source.SeekCount;
        await session.GetWindowAsync(new(MediaTime.Zero, 1, 16), false);
        Assert.True(source.SeekCount > afterEviction);
    }

    /// <summary>短源首帧延迟及 EOF 的补零缓存逐字节匹配旧逐窗 FFT，不改变绝对中心网格。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1115)]
    public async Task CachedBoundaryColumnsMatchReferenceFftWithAndWithoutPriming(long firstSample)
    {
        const long END_SAMPLE = WaveformAnalyzer.SAMPLE_RATE;
        static float Tone(long sample) => (float)(0.125 * Math.Sin(sample * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
        var source = new WindowAudioSource(Tone, END_SAMPLE, 317, firstSample);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(1));
        await session.PrepareCacheAsync();
        foreach (var resolution in new[] { 512, 32768 })
        {
            var request = new WaveformAnalysisRequest(MediaTime.Zero, resolution,
                checked((int)((END_SAMPLE + resolution - 1) / resolution)));
            var spectrum = Assert.IsType<SpectrogramData>((await session.GetLayersAsync(request, false, true)).Spectrogram);
            var firstCenter = (spectrum.Start + spectrum.ColumnDuration / 2)
                .ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
            var stride = checked((int)spectrum.ColumnDuration
                .ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value);
            float Read(long sample) => sample < firstSample || sample >= END_SAMPLE ? 0 : Tone(sample);
            var expected = AudioSpectrumWindowAnalyzer.Analyze(Read, static _ => { }, MediaTime.Zero,
                firstCenter, stride, spectrum.Width, 0, () => END_SAMPLE, static () => { });

            Assert.Equal(0, firstCenter);
            Assert.Equal(expected.Levels.ToArray(), spectrum.Levels.ToArray());
        }
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(END_SAMPLE - firstSample, source.FramesRead);
    }

    /// <summary>不同块大小和正负媒体原点仍使用同一绝对频谱中心网格。</summary>
    [Theory]
    [InlineData(192000)]
    [InlineData(-192000)]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task FrequencyWindowsUseOneMediaHopGridAcrossChunkingAndOrigins(long originSamples)
    {
        const int SECONDS = 8;
        var origin = new MediaTime(originSamples, WaveformAnalyzer.SAMPLE_RATE);
        var end = originSamples + SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE;
        static float Sample(long index) => (float)(0.5 * Math.Sin(index * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
        await using var whole = new AudioAnalysisSession(_ => new WindowAudioSource(Sample, end, firstSample: originSamples),
            new(origin), new(SECONDS));
        await using var split = new AudioAnalysisSession(_ => new WindowAudioSource(Sample, end, 317, originSamples),
            new(origin), new(SECONDS));
        var full = (await whole.GetWindowAsync(new(MediaTime.Zero, 1024, 375), true)).Spectrogram!;
        var local = (await split.GetWindowAsync(new(new(2), 1024, 128), true)).Spectrogram!;
        Assert.Equal(new MediaTime(256, SpectrogramAnalyzer.SAMPLE_RATE), local.ColumnDuration);
        for (var column = 0; column < local.Width; column++)
        {
            var center = local.Start + local.ColumnDuration * column + local.ColumnDuration / 2;
            var mediaCenter = (center + origin).ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
            Assert.Equal(0, mediaCenter % SpectrogramAnalyzer.HOP_SIZE);
            var other = (int)(center - full.Start).ToTimestamp(new(full.ColumnDuration.Numerator, full.ColumnDuration.Denominator),
                MediaTimeRounding.FLOOR).Value;
            Assert.InRange(other, 0, full.Width - 1);
            for (var row = 0; row < local.Height; row++)
            {
                Assert.Equal(full.Levels.Span[row * full.Width + other], local.Levels.Span[row * local.Width + column]);
            }
        }
    }

    /// <summary>固定相位低通抑制高频折叠，不把 15kHz 能量混入有效频率行。</summary>
    [Fact]
    public async Task FixedPhaseLowPassRejectsHighFrequencyAliasing()
    {
        static WindowAudioSource Tone(int frequency) => new(index => (float)(0.5 * Math.Sin(index * 2 * Math.PI * frequency / WaveformAnalyzer.SAMPLE_RATE)),
            WaveformAnalyzer.SAMPLE_RATE * 2L);
        await using var low = new AudioAnalysisSession(_ => Tone(1000), new(MediaTime.Zero), new(2));
        await using var high = new AudioAnalysisSession(_ => Tone(15000), new(MediaTime.Zero), new(2));
        var request = new WaveformAnalysisRequest(new(1, 2), 512, 64);
        var expected = (await low.GetWindowAsync(request, true)).Spectrogram!;
        var filtered = (await high.GetWindowAsync(request, true)).Spectrogram!;
        var expectedRow = (int)(Math.Log(1000.0 / 40) / Math.Log(8000.0 / 40) * expected.Height);
        Assert.True(expected.Levels.Span[expectedRow * expected.Width + expected.Width / 2] > 150);
        Assert.True(filtered.Levels.Span[expectedRow * filtered.Width + filtered.Width / 2] < 80);
    }

    /// <summary>极细窗口替换不取消共享细节解码器，关闭时等待其释放。</summary>
    [Fact]
    public async Task SupersedingFineWorkKeepsTheDetailDecoderUsableAndClosingDrainsIt()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        var source = new WindowAudioSource(_ => 0.25F, WaveformAnalyzer.SAMPLE_RATE * 4L)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        await using var session = new AudioAnalysisSession(_ => throw new InvalidOperationException("不得启动完整扫描。"),
            new(MediaTime.Zero), new(4), detailSourceFactory: _ =>
            {
                Interlocked.Increment(ref opened);
                return source;
            });
        var old = session.GetWindowAsync(new(new(1), 32, 100), false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = session.GetWindowAsync(new(new(2), 32, 100), false);
        release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        var result = await latest.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MediaTime(2), result.Waveform.Start);
        Assert.Equal(1, opened);
        Assert.Equal(0, source.CancelCount);
        await session.DisposeAsync();
        Assert.Equal(1, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
    }
}
