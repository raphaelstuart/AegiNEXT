using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证持久化准确摘要在全片、局部和分辨率切换时只读取缓存。</summary>
public sealed class AudioAnalysisZoomReuseTests
{
    private const long CACHE_BYTES = 2L * 1024 * 1024;

    /// <summary>完整粗视图与局部细视图交替时，恢复完整视图不产生新的 PCM 读取或定位。</summary>
    [Fact]
    public async Task FullWindowAndLocalOrdinaryZoomShareTheCompletedCacheWithoutAdditionalIo()
    {
        const int SECONDS = 8;
        var source = new WindowAudioSource(Tone, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(SECONDS), CACHE_BYTES);
        var request = FullRequest(SECONDS, 16384);
        var full = await session.GetWindowAsync(request, true);
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;

        await session.GetWindowAsync(new(new(4), 512, 256), true);
        var restored = await session.GetWindowAsync(request, true);

        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.Equal(full.Waveform.Peaks.ToArray(), restored.Waveform.Peaks.ToArray());
        Assert.Equal(full.Spectrogram!.Levels.ToArray(), restored.Spectrogram!.Levels.ToArray());
        Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
    }

    /// <summary>基础波形摘要精确保留单样本峰值，粗细分辨率转换均无需重新解码已分析范围。</summary>
    [Fact]
    public async Task WaveformSummariesPreserveBoundaryImpulsesAcrossCoarserAndFinerResolutionsWithoutIo()
    {
        const int SECONDS = 8;
        static float Sample(long index) => index switch
        {
            8191 => -0.8F,
            8192 => 0.7F,
            32767 => -0.6F,
            32768 => 0.4F,
            192000 => 0.9F,
            383999 => -0.75F,
            _ => 0
        };
        var source = new WindowAudioSource(Sample, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(SECONDS), CACHE_BYTES);
        await session.GetWindowAsync(FullRequest(SECONDS, 8192), false);
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;
        var impulses = new (long Sample, float Value)[]
        {
            (8191, -0.8F), (8192, 0.7F), (32767, -0.6F), (32768, 0.4F),
            (192000, 0.9F), (383999, -0.75F)
        };
        foreach (var resolution in new[] { 32768, 4096, 512, 8192 })
        {
            var result = await session.GetWindowAsync(FullRequest(SECONDS, resolution), false);
            var expected = new float[result.Waveform.BucketCount * 2];
            foreach (var impulse in impulses)
            {
                var bucket = (int)(impulse.Sample / resolution);
                expected[bucket * 2] = Math.Min(expected[bucket * 2], impulse.Value);
                expected[bucket * 2 + 1] = Math.Max(expected[bucket * 2 + 1], impulse.Value);
            }
            Assert.Equal(expected, result.Waveform.Peaks.ToArray());
            Assert.Equal(reads, source.ReadCount);
            Assert.Equal(frames, source.FramesRead);
            Assert.Equal(seeks, source.SeekCount);
            Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
        }
    }

    /// <summary>粗谱列复用较细媒体中心网格的相同 FFT 结果，无需再次定位或读取。</summary>
    [Fact]
    public async Task CoarserSpectrumCopiesTheFineCenterSubsetWithoutAdditionalIo()
    {
        const int SECONDS = 8;
        var source = new WindowAudioSource(Tone, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(SECONDS), CACHE_BYTES);
        var fineResult = await session.GetLayersAsync(FullRequest(SECONDS, 2048), false, true);
        var fine = Assert.IsType<SpectrogramData>(fineResult.Spectrogram);
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;

        var coarseResult = await session.GetLayersAsync(FullRequest(SECONDS, 8192), false, true);

        var coarse = Assert.IsType<SpectrogramData>(coarseResult.Spectrogram);
        Assert.True(coarse.ColumnDuration > fine.ColumnDuration);
        Assert.Contains(coarse.Levels.ToArray(), value => value > 100);
        var firstFineCenter = fine.Start + fine.ColumnDuration / 2;
        for (var column = 0; column < coarse.Width; column++)
        {
            var center = coarse.Start + coarse.ColumnDuration * column + coarse.ColumnDuration / 2;
            if (center >= new MediaTime(SECONDS))
            {
                for (var row = 0; row < coarse.Height; row++)
                {
                    Assert.Equal(0, coarse.Levels.Span[row * coarse.Width + column]);
                }
                continue;
            }
            var fineColumn = checked((int)((center - firstFineCenter)
                .ToTimestamp(new(fine.ColumnDuration.Numerator, fine.ColumnDuration.Denominator), MediaTimeRounding.FLOOR).Value));
            Assert.InRange(fineColumn, 0, fine.Width - 1);
            Assert.Equal(center, firstFineCenter + fine.ColumnDuration * fineColumn);
            for (var row = 0; row < coarse.Height; row++)
            {
                Assert.Equal(fine.Levels.Span[row * fine.Width + fineColumn], coarse.Levels.Span[row * coarse.Width + column]);
            }
        }
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
    }

    /// <summary>复用摘要时，PTS 缺口保持静默，已确认 EOF 后的请求和粗全片恢复均不重新读取源。</summary>
    [Fact]
    public async Task SummaryReuseKeepsTimestampGapsAndConfirmedEofSilentWithoutAdditionalIo()
    {
        var source = new WindowAudioSource(_ => 0.25F, 3L * WaveformAnalyzer.SAMPLE_RATE)
        {
            Gap = (WaveformAnalyzer.SAMPLE_RATE, 2L * WaveformAnalyzer.SAMPLE_RATE)
        };
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(8), CACHE_BYTES);
        await session.GetWindowAsync(FullRequest(8, 8192), true);
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;
        var gap = await session.GetWindowAsync(new(new(5, 4), 512, 64), true);
        Assert.All(gap.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(gap.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        var pastEnd = await session.GetWindowAsync(new(new(4), 512, 128), true);
        Assert.All(pastEnd.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(pastEnd.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        var coarse = await session.GetWindowAsync(FullRequest(8, 8192), true);
        var waveform = coarse.Waveform;
        var bucketDuration = new MediaTime(waveform.SamplesPerBucket, WaveformAnalyzer.SAMPLE_RATE);
        for (var bucket = 0; bucket < waveform.BucketCount; bucket++)
        {
            var start = waveform.Start + bucketDuration * bucket;
            if (start >= new MediaTime(3) || start >= new MediaTime(1) && start + bucketDuration <= new MediaTime(2))
            {
                Assert.Equal(0, waveform.Peaks.Span[bucket * 2]);
                Assert.Equal(0, waveform.Peaks.Span[bucket * 2 + 1]);
            }
        }
        var spectrum = Assert.IsType<SpectrogramData>(coarse.Spectrogram);
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center < new MediaTime(3) && (center < new MediaTime(11, 10) || center > new MediaTime(19, 10)))
            {
                continue;
            }
            for (var row = 0; row < spectrum.Height; row++)
            {
                Assert.Equal(0, spectrum.Levels.Span[row * spectrum.Width + column]);
            }
        }
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
    }

    private static WaveformAnalysisRequest FullRequest(int seconds, int samplesPerBucket)
    {
        var samples = (long)seconds * WaveformAnalyzer.SAMPLE_RATE;
        return new(MediaTime.Zero, samplesPerBucket, checked((int)((samples + samplesPerBucket - 1) / samplesPerBucket)));
    }

    private static float Tone(long sample)
    {
        return (float)(0.5 * Math.Sin(sample * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
    }
}
