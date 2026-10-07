using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证缩略分析的稀疏读取、频率范围以及与精确分析的缓存隔离。</summary>
public sealed class AudioAnalysisPreviewTests
{
    private const long CACHE_BYTES = 2L * 1024 * 1024;
    private const int PREVIEW_SAMPLES_PER_BUCKET = 262144;

    /// <summary>冷全片缩略请求按可见采样窗口读取 PCM，并保留 6 kHz 频带和媒体映射。</summary>
    [Fact]
    public async Task ColdFullPreviewReadsSparseWindowsAndPreservesTheSixKilohertzBand()
    {
        const int SECONDS = 120;
        var origin = new MediaTime(21, 1000);
        var source = new WindowAudioSource(Tone,
            ((long)SECONDS * WaveformAnalyzer.SAMPLE_RATE) + 1008);
        await using var session = new AudioAnalysisSession(_ => source, new(origin), new(SECONDS), CACHE_BYTES);
        var request = FullRequest(SECONDS);

        var result = await session.GetWindowAsync(request, true);

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
        // 22 波形桶与约 30 谱列只需要局部窗口，1M 帧上限包含解码块对齐开销。
        Assert.InRange(source.FramesRead, 1, 1_000_000);
        Assert.InRange(source.ReadCount, 1, 260);
        Assert.InRange(source.SeekCount, 1, request.BucketCount + spectrum.Width + 2);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
    }

    /// <summary>相同范围与分辨率的两种模式各自保存峰值，精确模式始终能恢复采样窗外的单样本脉冲。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewAndExactCachesKeepTheirOwnPeaksInEitherRequestOrder(bool previewFirst)
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
        var preview = new WaveformAnalysisRequest(MediaTime.Zero, 16384, 3, AudioAnalysisMode.PREVIEW);
        Assert.Equal(AudioAnalysisMode.EXACT, exact.Mode);
        Assert.NotEqual(exact, preview);

        var first = await session.GetWindowAsync(previewFirst ? preview : exact, false);
        var second = await session.GetWindowAsync(previewFirst ? exact : preview, false);

        Assert.Equal(previewFirst ? 0.7F : 0.9F, first.Waveform.Peaks.Span[1]);
        var previewPeak = previewFirst ? first.Waveform.Peaks.Span[1] : second.Waveform.Peaks.Span[1];
        Assert.Equal(0.9F, previewFirst ? second.Waveform.Peaks.Span[1] : first.Waveform.Peaks.Span[1]);
        Assert.True(previewPeak is 0.7F or 0.9F);
        Assert.Equal(0, first.Waveform.Peaks.Span[0]);
        Assert.Equal(0, second.Waveform.Peaks.Span[0]);
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;
        var restoredExact = await session.GetWindowAsync(exact, false);
        var restoredPreview = await session.GetWindowAsync(preview, false);
        Assert.Equal(0.9F, restoredExact.Waveform.Peaks.Span[1]);
        Assert.Equal(previewPeak, restoredPreview.Waveform.Peaks.Span[1]);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
    }

    /// <summary>缩略全片与精确局部交替后，同一缩略摘要不再读取或定位源。</summary>
    [Fact]
    public async Task PreviewSurvivesALocalExactRequestWithoutAdditionalIoOnRestore()
    {
        const int SECONDS = 120;
        var source = new WindowAudioSource(Tone, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(SECONDS), CACHE_BYTES);
        var request = FullRequest(SECONDS);
        var preview = await session.GetWindowAsync(request, true);
        var local = await session.GetWindowAsync(new(new(60), 512, 128), true);
        Assert.Equal(AudioAnalysisMode.EXACT, local.Waveform.Request.Mode);
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;

        var restored = await session.GetWindowAsync(request, true);

        Assert.Equal(preview.Waveform.Peaks.ToArray(), restored.Waveform.Peaks.ToArray());
        Assert.Equal(preview.Spectrogram!.Levels.ToArray(), restored.Spectrogram!.Levels.ToArray());
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
    }

    /// <summary>粗缩略摘要可连续放大两级近似重采样，精确局部仍重新取得窗外瞬态峰值。</summary>
    [Fact]
    public async Task CoarsePreviewResamplesBothLayersAcrossTwoFinerLevelsWithoutIoAndKeepsExactImpulses()
    {
        const int SECONDS = 120;
        const int ORIGIN_SAMPLES = 1008;
        const long IMPULSE_SAMPLE = 60L * WaveformAnalyzer.SAMPLE_RATE + ORIGIN_SAMPLES + 1;
        static float Sample(long index)
        {
            if (index == IMPULSE_SAMPLE)
            {
                return 0.9F;
            }
            var bucket = Math.Max(0, index - ORIGIN_SAMPLES) / PREVIEW_SAMPLES_PER_BUCKET;
            return Tone(index) * (float)(0.2 + bucket % 8 * 0.04);
        }
        var origin = new MediaTime(ORIGIN_SAMPLES, WaveformAnalyzer.SAMPLE_RATE);
        var source = new WindowAudioSource(Sample, (long)SECONDS * WaveformAnalyzer.SAMPLE_RATE + ORIGIN_SAMPLES);
        await using var session = new AudioAnalysisSession(_ => source, new(origin), new(SECONDS), CACHE_BYTES);
        var coarse = await session.GetWindowAsync(FullRequest(SECONDS), true);
        var coarseSpectrum = Assert.IsType<SpectrogramData>(coarse.Spectrogram);
        var cachedSpectra = new List<SpectrogramData> { coarseSpectrum };
        Assert.DoesNotContain(coarse.Waveform.Peaks.ToArray(), value => value >= 0.9F);
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;
        foreach (var resolution in new[] { 131072, 65536 })
        {
            var request = FullRequest(SECONDS, resolution);

            var fine = await session.GetWindowAsync(request, true);

            Assert.Equal(AudioAnalysisMode.PREVIEW, fine.Waveform.Request.Mode);
            Assert.Equal(MediaTime.Zero, fine.Waveform.Start);
            Assert.True(fine.Waveform.End >= new MediaTime(SECONDS));
            for (var bucket = 0; bucket < fine.Waveform.BucketCount; bucket++)
            {
                var coarseBucket = (int)((long)bucket * resolution / PREVIEW_SAMPLES_PER_BUCKET);
                Assert.Equal(coarse.Waveform.Peaks.Span[coarseBucket * 2], fine.Waveform.Peaks.Span[bucket * 2]);
                Assert.Equal(coarse.Waveform.Peaks.Span[coarseBucket * 2 + 1], fine.Waveform.Peaks.Span[bucket * 2 + 1]);
            }
            var fineSpectrum = Assert.IsType<SpectrogramData>(fine.Spectrogram);
            Assert.True(fineSpectrum.End >= new MediaTime(SECONDS));
            for (var column = 0; column < fineSpectrum.Width; column++)
            {
                var center = fineSpectrum.Start + fineSpectrum.ColumnDuration * column + fineSpectrum.ColumnDuration / 2;
                if (center < MediaTime.Zero || center >= new MediaTime(SECONDS))
                {
                    for (var row = 0; row < fineSpectrum.Height; row++)
                    {
                        Assert.Equal(0, fineSpectrum.Levels.Span[row * fineSpectrum.Width + column]);
                    }
                    continue;
                }
                var nearestColumns = new List<(SpectrogramData Spectrum, int Column)>();
                foreach (var basis in cachedSpectra)
                {
                    var distance = long.MaxValue;
                    var basisNearest = new List<int>();
                    for (var candidate = 0; candidate < basis.Width; candidate++)
                    {
                        var candidateCenter = basis.Start + basis.ColumnDuration * candidate + basis.ColumnDuration / 2;
                        if (candidateCenter < MediaTime.Zero || candidateCenter >= new MediaTime(SECONDS))
                        {
                            continue;
                        }
                        var candidateDistance = Math.Abs((candidateCenter - center)
                            .ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value);
                        if (candidateDistance < distance)
                        {
                            distance = candidateDistance;
                            basisNearest.Clear();
                        }
                        if (candidateDistance == distance)
                        {
                            basisNearest.Add(candidate);
                        }
                    }
                    foreach (var candidate in basisNearest)
                    {
                        nearestColumns.Add((basis, candidate));
                    }
                }
                Assert.NotEmpty(nearestColumns);
                Assert.Contains(nearestColumns, candidate => Enumerable.Range(0, fineSpectrum.Height).All(row =>
                    fineSpectrum.Levels.Span[row * fineSpectrum.Width + column]
                    == candidate.Spectrum.Levels.Span[row * candidate.Spectrum.Width + candidate.Column]));
            }
            cachedSpectra.Add(fineSpectrum);
            Assert.Equal(reads, source.ReadCount);
            Assert.Equal(frames, source.FramesRead);
            Assert.Equal(seeks, source.SeekCount);
            Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
        }

        var exact = await session.GetWindowAsync(new(new(60), 512, 32), false);

        Assert.Equal(AudioAnalysisMode.EXACT, exact.Waveform.Request.Mode);
        Assert.Equal(0.9F, exact.Waveform.Peaks.Span[1]);
        Assert.True(source.FramesRead > frames);
    }

    /// <summary>已有精确全片摘要可跨 PCM tile 网格复用到缩略全片，两层均不新增解码或定位。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1008)]
    public async Task ExactFullRangeSuppliesBothPreviewLayersWithoutAdditionalIo(int originSamples)
    {
        const int SECONDS = 120;
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
        var exactRequest = new WaveformAnalysisRequest(MediaTime.Zero, EXACT_SAMPLES_PER_BUCKET,
            checked((int)((sampleCount + EXACT_SAMPLES_PER_BUCKET - 1) / EXACT_SAMPLES_PER_BUCKET)));
        var exact = await session.GetWindowAsync(exactRequest, true);
        Assert.True(source.FramesRead * sizeof(float) > CACHE_BYTES * 4);
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;

        var preview = await session.GetWindowAsync(FullRequest(SECONDS), true);

        Assert.Equal(AudioAnalysisMode.PREVIEW, preview.Waveform.Request.Mode);
        Assert.True(preview.Waveform.End >= new MediaTime(SECONDS));
        var ratio = PREVIEW_SAMPLES_PER_BUCKET / EXACT_SAMPLES_PER_BUCKET;
        for (var bucket = 0; bucket < preview.Waveform.BucketCount; bucket++)
        {
            var low = 0F;
            var high = 0F;
            var after = Math.Min((bucket + 1) * ratio, exact.Waveform.BucketCount);
            for (var fineBucket = bucket * ratio; fineBucket < after; fineBucket++)
            {
                low = Math.Min(low, exact.Waveform.Peaks.Span[fineBucket * 2]);
                high = Math.Max(high, exact.Waveform.Peaks.Span[fineBucket * 2 + 1]);
            }
            Assert.Equal(low, preview.Waveform.Peaks.Span[bucket * 2]);
            Assert.Equal(high, preview.Waveform.Peaks.Span[bucket * 2 + 1]);
        }
        var exactSpectrum = Assert.IsType<SpectrogramData>(exact.Spectrogram);
        var previewSpectrum = Assert.IsType<SpectrogramData>(preview.Spectrogram);
        var firstExactCenter = exactSpectrum.Start + exactSpectrum.ColumnDuration / 2;
        for (var column = 0; column < previewSpectrum.Width; column++)
        {
            var center = previewSpectrum.Start + previewSpectrum.ColumnDuration * column + previewSpectrum.ColumnDuration / 2;
            var exactColumn = checked((int)((center - firstExactCenter).ToTimestamp(
                new(exactSpectrum.ColumnDuration.Numerator, exactSpectrum.ColumnDuration.Denominator), MediaTimeRounding.FLOOR).Value));
            Assert.InRange(exactColumn, 0, exactSpectrum.Width - 1);
            Assert.Equal(center, firstExactCenter + exactSpectrum.ColumnDuration * exactColumn);
            for (var row = 0; row < previewSpectrum.Height; row++)
            {
                Assert.Equal(exactSpectrum.Levels.Span[row * exactSpectrum.Width + exactColumn],
                    previewSpectrum.Levels.Span[row * previewSpectrum.Width + column]);
            }
        }
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);

        var restoredExact = await session.GetWindowAsync(exactRequest, true);

        Assert.Equal(exact.Waveform.Peaks.ToArray(), restoredExact.Waveform.Peaks.ToArray());
        Assert.Equal(exactSpectrum.Levels.ToArray(), restoredExact.Spectrogram!.Levels.ToArray());
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
    }

    /// <summary>缩略采样中的 PTS 缺口与真实 EOF 保持静默，确认 EOF 后的远端请求不访问源。</summary>
    [Fact]
    public async Task PreviewKeepsTimestampGapsAndEarlyEofSilent()
    {
        var source = new WindowAudioSource(Tone, 45L * WaveformAnalyzer.SAMPLE_RATE)
        {
            Gap = (30L * WaveformAnalyzer.SAMPLE_RATE, 33L * WaveformAnalyzer.SAMPLE_RATE)
        };
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(120), CACHE_BYTES);

        var result = await session.GetWindowAsync(FullRequest(120), true);

        var waveform = result.Waveform;
        for (var bucket = 0; bucket < waveform.BucketCount; bucket++)
        {
            var center = waveform.Start + new MediaTime((long)waveform.SamplesPerBucket * bucket
                + waveform.SamplesPerBucket / 2, WaveformAnalyzer.SAMPLE_RATE);
            if (center >= new MediaTime(45) || center >= new MediaTime(301, 10) && center <= new MediaTime(329, 10))
            {
                Assert.Equal(0, waveform.Peaks.Span[bucket * 2]);
                Assert.Equal(0, waveform.Peaks.Span[bucket * 2 + 1]);
            }
        }
        var spectrum = Assert.IsType<SpectrogramData>(result.Spectrogram);
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center < new MediaTime(45) && (center < new MediaTime(301, 10) || center > new MediaTime(329, 10)))
            {
                continue;
            }
            for (var row = 0; row < spectrum.Height; row++)
            {
                Assert.Equal(0, spectrum.Levels.Span[row * spectrum.Width + column]);
            }
        }
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var seeks = source.SeekCount;
        var pastEnd = await session.GetWindowAsync(new(new(100), 16384, 32, AudioAnalysisMode.PREVIEW), true);
        Assert.All(pastEnd.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(pastEnd.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
    }

    /// <summary>已经确认 EOF 后，新分辨率的边界 tile 仍能完成，桶内剩余范围采用零填充。</summary>
    [Theory]
    [InlineData(AudioAnalysisMode.EXACT, 256, 512)]
    [InlineData(AudioAnalysisMode.PREVIEW, 262144, 2)]
    public async Task ANewResolutionAtConfirmedEofCompletesWithoutAnEmptySpanLoop(
        AudioAnalysisMode mode, int samplesPerBucket, int bucketCount)
    {
        var source = new WindowAudioSource(_ => 0.25F, 45L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(120), CACHE_BYTES);
        await session.GetWindowAsync(new(new(44), 512, 256), false);
        var request = new WaveformAnalysisRequest(new(44), samplesPerBucket, bucketCount, mode);

        var result = await session.GetWindowAsync(request, false).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(request, result.Waveform.Request);
        var bucketDuration = new MediaTime(samplesPerBucket, WaveformAnalyzer.SAMPLE_RATE);
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = request.Start + bucketDuration * bucket;
            if (start >= new MediaTime(45))
            {
                Assert.Equal(0, result.Waveform.Peaks.Span[bucket * 2]);
                Assert.Equal(0, result.Waveform.Peaks.Span[bucket * 2 + 1]);
            }
        }
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
    }

    /// <summary>批量复制在精确与缩略路径中仍拒绝实际 PCM slice 内的非有限值。</summary>
    [Theory]
    [InlineData(AudioAnalysisMode.EXACT, float.NaN)]
    [InlineData(AudioAnalysisMode.PREVIEW, float.PositiveInfinity)]
    public async Task NonFinitePcmIsRejectedBeforePublishingEitherMode(AudioAnalysisMode mode, float invalidSample)
    {
        var source = new WindowAudioSource(index => index == 8192 ? invalidSample : 0.25F,
            WaveformAnalyzer.SAMPLE_RATE, 317);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(1), CACHE_BYTES);
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 16384, 3, mode);

        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetWindowAsync(request, false));
    }

    private static WaveformAnalysisRequest FullRequest(int seconds, int samplesPerBucket = PREVIEW_SAMPLES_PER_BUCKET)
    {
        var samples = (long)seconds * WaveformAnalyzer.SAMPLE_RATE;
        return new(MediaTime.Zero, samplesPerBucket,
            checked((int)((samples + samplesPerBucket - 1) / samplesPerBucket)),
            AudioAnalysisMode.PREVIEW);
    }

    private static float Tone(long sample)
    {
        return (float)(0.5 * Math.Sin(sample * 2 * Math.PI * 6000 / WaveformAnalyzer.SAMPLE_RATE));
    }
}
