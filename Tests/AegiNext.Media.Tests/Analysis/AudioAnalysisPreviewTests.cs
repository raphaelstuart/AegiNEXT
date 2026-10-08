using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证缩略与精确视图共享完整持久摘要，并保留频率范围、瞬态与媒体时间映射。</summary>
public sealed class AudioAnalysisPreviewTests
{
    private const long CACHE_BYTES = 2L * 1024 * 1024;
    private const int PREVIEW_SAMPLES_PER_BUCKET = 32768;

    [Fact]
    public async Task ColdPreviewBuildsTheCompleteCacheAndPreservesTheSixKilohertzBand()
    {
        const int SECONDS = 12;
        const int ORIGIN_SAMPLES = 1008;
        var origin = new MediaTime(ORIGIN_SAMPLES, WaveformAnalyzer.SAMPLE_RATE);
        var source = new WindowAudioSource(Tone, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE + ORIGIN_SAMPLES);
        await using var session = new AudioAnalysisSession(_ => source, new(origin), new(SECONDS), CACHE_BYTES);
        var request = FullRequest(SECONDS);

        var result = await session.GetWindowAsync(request, true);
        await session.PrepareCacheAsync();

        Assert.True(session.IsCacheComplete);
        Assert.Equal(AudioAnalysisMode.PREVIEW, result.Waveform.Request.Mode);
        Assert.Equal(request.Start, result.Waveform.Start);
        Assert.True(result.Waveform.End >= new MediaTime(SECONDS));
        Assert.Equal(request.BucketCount, result.Waveform.BucketCount);
        for (var bucket = 0; bucket < result.Waveform.BucketCount; bucket++)
        {
            Assert.True(result.Waveform.Peaks.Span[bucket * 2] < -0.45F);
            Assert.True(result.Waveform.Peaks.Span[bucket * 2 + 1] > 0.45F);
        }
        var spectrum = Assert.IsType<SpectrogramData>(result.Spectrogram);
        Assert.Equal(SpectrogramAnalyzer.FREQUENCY_BINS, spectrum.Height);
        Assert.True(spectrum.End >= new MediaTime(SECONDS));
        var toneRow = (int)(Math.Log(6000.0 / 40) / Math.Log(8000.0 / 40) * spectrum.Height);
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center > new MediaTime(1, 10) && center < new MediaTime(SECONDS))
            {
                Assert.True(spectrum.Levels.Span[toneRow * spectrum.Width + column] > 100);
            }
        }
        Assert.Equal((long)SECONDS * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        Assert.Equal(1, source.SeekCount);
        Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewAndExactModesShareAccuratePeaksInEitherRequestOrder(bool previewFirst)
    {
        static float Sample(long index) => index switch
        {
            1 => 0.9F,
            8192 => 0.7F,
            _ => 0
        };
        var source = new WindowAudioSource(Sample, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(1), CACHE_BYTES);
        var exact = new WaveformAnalysisRequest(MediaTime.Zero, 16384, 3);
        var preview = new WaveformAnalysisRequest(exact.Start, exact.SamplesPerBucket, exact.BucketCount, AudioAnalysisMode.PREVIEW);

        var first = await session.GetWindowAsync(previewFirst ? preview : exact, false);
        var second = await session.GetWindowAsync(previewFirst ? exact : preview, false);

        Assert.Equal(previewFirst ? preview : exact, first.Waveform.Request);
        Assert.Equal(previewFirst ? exact : preview, second.Waveform.Request);
        Assert.Equal(0.9F, first.Waveform.Peaks.Span[1]);
        Assert.Equal(first.Waveform.Peaks.ToArray(), second.Waveform.Peaks.ToArray());
        var reads = source.ReadCount;
        await session.GetWindowAsync(exact, false);
        await session.GetWindowAsync(preview, false);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
    }

    [Fact]
    public async Task PreviewAndOrdinaryLocalZoomUseTheSameCompletedCacheWithoutAdditionalIo()
    {
        const int SECONDS = 8;
        var source = new WindowAudioSource(Tone, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(SECONDS), CACHE_BYTES);
        await session.PrepareCacheAsync();
        var request = FullRequest(SECONDS);
        var preview = await session.GetWindowAsync(request, true);
        var reads = source.ReadCount;
        var local = await session.GetWindowAsync(new(new(4), 512, 128), true);
        var restored = await session.GetWindowAsync(request, true);
        Assert.Equal(AudioAnalysisMode.EXACT, local.Waveform.Request.Mode);
        Assert.Equal(preview.Waveform.Peaks.ToArray(), restored.Waveform.Peaks.ToArray());
        Assert.Equal(preview.Spectrogram!.Levels.ToArray(), restored.Spectrogram!.Levels.ToArray());
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
        Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
    }

    [Fact]
    public async Task PreviewZoomReadsExactPyramidExtremaAndSpectrumSubsetsWithoutLosingImpulses()
    {
        const int SECONDS = 8;
        const int ORIGIN_SAMPLES = 1008;
        const long IMPULSE_SAMPLE = 4L * WaveformAnalyzer.SAMPLE_RATE + ORIGIN_SAMPLES + 1;
        static float Sample(long index)
        {
            if (index == IMPULSE_SAMPLE)
            {
                return 0.9F;
            }
            var bucket = Math.Max(0, index - ORIGIN_SAMPLES) / PREVIEW_SAMPLES_PER_BUCKET;
            return Tone(index) * (float)(0.2 + bucket % 8 * 0.04);
        }
        var source = new WindowAudioSource(Sample, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE + ORIGIN_SAMPLES);
        var origin = new MediaTime(ORIGIN_SAMPLES, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(origin), new(SECONDS), CACHE_BYTES,
            detailSourceFactory: _ => throw new InvalidOperationException("Ordinary zoom must use the full cache."));
        await session.PrepareCacheAsync();
        var coarse = await session.GetWindowAsync(FullRequest(SECONDS), true);
        var reads = source.ReadCount;
        Assert.Contains(0.9F, coarse.Waveform.Peaks.ToArray());
        foreach (var resolution in new[] { 16384, 8192 })
        {
            var fine = await session.GetWindowAsync(FullRequest(SECONDS, resolution), true);
            Assert.Equal(AudioAnalysisMode.PREVIEW, fine.Waveform.Request.Mode);
            Assert.Contains(0.9F, fine.Waveform.Peaks.ToArray());
            AssertWaveformAggregation(coarse.Waveform, fine.Waveform);
            AssertSpectrumSubset(coarse.Spectrogram!, fine.Spectrogram!, new(SECONDS));
            Assert.Equal(reads, source.ReadCount);
        }
        var exact = await session.GetWindowAsync(new(new(4), 512, 32), false);
        Assert.Equal(0.9F, exact.Waveform.Peaks.Span[1]);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1008)]
    public async Task ExactFullRangeSuppliesBothPreviewLayersWithoutAdditionalIo(int originSamples)
    {
        const int SECONDS = 8;
        const int EXACT_SAMPLES_PER_BUCKET = 4096;
        const long LAST_SAMPLE = (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE - 1;
        var sampleCount = (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE;
        var source = new WindowAudioSource(index => (index - originSamples) switch
        {
            1 => 0.9F,
            65535 => -0.8F,
            LAST_SAMPLE => -0.7F,
            _ => Tone(index)
        }, sampleCount + originSamples);
        var origin = new MediaTime(originSamples, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(origin), new(SECONDS), CACHE_BYTES);
        await session.PrepareCacheAsync();
        var exactRequest = new WaveformAnalysisRequest(MediaTime.Zero, EXACT_SAMPLES_PER_BUCKET,
            checked((int)((sampleCount + EXACT_SAMPLES_PER_BUCKET - 1) / EXACT_SAMPLES_PER_BUCKET)));
        var exact = await session.GetWindowAsync(exactRequest, true);
        var reads = source.ReadCount;

        var preview = await session.GetWindowAsync(FullRequest(SECONDS), true);

        Assert.Equal(AudioAnalysisMode.PREVIEW, preview.Waveform.Request.Mode);
        AssertWaveformAggregation(preview.Waveform, exact.Waveform);
        AssertSpectrumSubset(preview.Spectrogram!, exact.Spectrogram!, new(SECONDS));
        var restored = await session.GetWindowAsync(exactRequest, true);
        Assert.Equal(exact.Waveform.Peaks.ToArray(), restored.Waveform.Peaks.ToArray());
        Assert.Equal(exact.Spectrogram!.Levels.ToArray(), restored.Spectrogram!.Levels.ToArray());
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
    }

    [Fact]
    public async Task PreviewKeepsTimestampGapsAndEarlyEofSilent()
    {
        var source = new WindowAudioSource(Tone, 3L * WaveformAnalyzer.SAMPLE_RATE)
        {
            Gap = (WaveformAnalyzer.SAMPLE_RATE, 2L * WaveformAnalyzer.SAMPLE_RATE)
        };
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(8), CACHE_BYTES);
        await session.PrepareCacheAsync();
        var result = await session.GetWindowAsync(FullRequest(8), true);
        var waveform = result.Waveform;
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
        var spectrum = result.Spectrogram!;
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center >= new MediaTime(3) || center >= new MediaTime(11, 10) && center <= new MediaTime(19, 10))
            {
                for (var row = 0; row < spectrum.Height; row++)
                {
                    Assert.Equal(0, spectrum.Levels.Span[row * spectrum.Width + column]);
                }
            }
        }
        var reads = source.ReadCount;
        var gap = await session.GetWindowAsync(new(new(5, 4), 512, 64), true);
        Assert.All(gap.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(gap.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        var pastEnd = await session.GetWindowAsync(new(new(6), 16384, 32, AudioAnalysisMode.PREVIEW), true);
        Assert.All(pastEnd.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(pastEnd.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(reads, source.ReadCount);
    }

    [Theory]
    [InlineData(AudioAnalysisMode.EXACT, 256, 512)]
    [InlineData(AudioAnalysisMode.PREVIEW, 32768, 4)]
    public async Task NewResolutionAtConfirmedEofCompletesWithZeroPaddedBuckets(
        AudioAnalysisMode mode, int samplesPerBucket, int bucketCount)
    {
        var source = new WindowAudioSource(_ => 0.25F, 3L * WaveformAnalyzer.SAMPLE_RATE);
        var detailSource = new WindowAudioSource(_ => 0.25F, 3L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(8), CACHE_BYTES,
            detailSourceFactory: _ => detailSource);
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var request = new WaveformAnalysisRequest(new(2), samplesPerBucket, bucketCount, mode);

        var result = await session.GetWindowAsync(request, false).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(request, result.Waveform.Request);
        var duration = new MediaTime(samplesPerBucket, WaveformAnalyzer.SAMPLE_RATE);
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            if (request.Start + duration * bucket >= new MediaTime(3))
            {
                Assert.Equal(0, result.Waveform.Peaks.Span[bucket * 2]);
                Assert.Equal(0, result.Waveform.Peaks.Span[bucket * 2 + 1]);
            }
        }
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(samplesPerBucket < 512, detailSource.ReadCount > 0);
        Assert.InRange(session.CachedBytes, 0, CACHE_BYTES);
    }

    [Theory]
    [InlineData(AudioAnalysisMode.EXACT, float.NaN)]
    [InlineData(AudioAnalysisMode.PREVIEW, float.PositiveInfinity)]
    public async Task NonFinitePcmIsRejectedBeforePublishingEitherMode(AudioAnalysisMode mode, float invalidSample)
    {
        await using var session = new AudioAnalysisSession(_ => new WindowAudioSource(
                index => index == 8192 ? invalidSample : 0.25F, WaveformAnalyzer.SAMPLE_RATE, 317),
            new(MediaTime.Zero), new(1), CACHE_BYTES);
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 16384, 3, mode);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetWindowAsync(request, false)
            .WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static void AssertWaveformAggregation(WaveformData coarse, WaveformData fine)
    {
        Assert.Equal(coarse.Start, fine.Start);
        var ratio = coarse.SamplesPerBucket / fine.SamplesPerBucket;
        for (var bucket = 0; bucket < coarse.BucketCount; bucket++)
        {
            var low = 0F;
            var high = 0F;
            var after = Math.Min((bucket + 1) * ratio, fine.BucketCount);
            for (var fineBucket = bucket * ratio; fineBucket < after; fineBucket++)
            {
                low = Math.Min(low, fine.Peaks.Span[fineBucket * 2]);
                high = Math.Max(high, fine.Peaks.Span[fineBucket * 2 + 1]);
            }
            Assert.Equal(low, coarse.Peaks.Span[bucket * 2]);
            Assert.Equal(high, coarse.Peaks.Span[bucket * 2 + 1]);
        }
    }

    private static void AssertSpectrumSubset(SpectrogramData coarse, SpectrogramData fine, MediaTime duration)
    {
        var firstFineCenter = fine.Start + fine.ColumnDuration / 2;
        for (var column = 0; column < coarse.Width; column++)
        {
            var center = coarse.Start + coarse.ColumnDuration * column + coarse.ColumnDuration / 2;
            if (center < MediaTime.Zero || center >= duration)
            {
                for (var row = 0; row < coarse.Height; row++)
                {
                    Assert.Equal(0, coarse.Levels.Span[row * coarse.Width + column]);
                }
                continue;
            }
            var fineColumn = checked((int)((center - firstFineCenter).ToTimestamp(
                new(fine.ColumnDuration.Numerator, fine.ColumnDuration.Denominator), MediaTimeRounding.FLOOR).Value));
            Assert.InRange(fineColumn, 0, fine.Width - 1);
            Assert.Equal(center, firstFineCenter + fine.ColumnDuration * fineColumn);
            for (var row = 0; row < coarse.Height; row++)
            {
                Assert.Equal(fine.Levels.Span[row * fine.Width + fineColumn], coarse.Levels.Span[row * coarse.Width + column]);
            }
        }
    }

    private static WaveformAnalysisRequest FullRequest(int seconds, int samplesPerBucket = PREVIEW_SAMPLES_PER_BUCKET)
    {
        var samples = (long)seconds * WaveformAnalyzer.SAMPLE_RATE;
        return new(MediaTime.Zero, samplesPerBucket, checked((int)((samples + samplesPerBucket - 1) / samplesPerBucket)),
            AudioAnalysisMode.PREVIEW);
    }

    private static float Tone(long sample)
    {
        return (float)(0.5 * Math.Sin(sample * 2 * Math.PI * 6000 / WaveformAnalyzer.SAMPLE_RATE));
    }
}
