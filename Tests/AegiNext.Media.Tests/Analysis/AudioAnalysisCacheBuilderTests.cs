using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisCacheBuilderTests
{
    [Theory]
    [InlineData(511)]
    [InlineData(-511)]
    public async Task SequentialScanAndParallelSegmentsPreserveProjectBucketsAndMediaCenters(long originSample)
    {
        const int SAMPLE_COUNT = 10013;
        var directory = TemporaryDirectory();
        var duration = new MediaTime(SAMPLE_COUNT, WaveformAnalyzer.SAMPLE_RATE);
        var mapping = new MediaTimelineMapping(new(originSample, WaveformAnalyzer.SAMPLE_RATE));
        float Sample(long sample) => sample - originSample switch
        {
            3071 => -0.9F,
            3072 => 0.8F,
            6143 => -0.7F,
            SAMPLE_COUNT - 1 => 0.95F,
            _ => (float)(0.3 * Math.Sin(sample * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE))
        };
        try
        {
            using var serial = new AudioAnalysisCacheStore(directory, "serial", mapping, duration);
            using var parallel = new AudioAnalysisCacheStore(directory, "parallel", mapping, duration);
            var serialSource = new WindowAudioSource(Sample, originSample + SAMPLE_COUNT, 317, originSample);
            var parallelSource = new WindowAudioSource(Sample, originSample + SAMPLE_COUNT, 733, originSample);
            await new AudioAnalysisCacheBuilder(_ => serialSource, mapping, duration, serial, 1, 3072).BuildAsync();
            await new AudioAnalysisCacheBuilder(_ => parallelSource, mapping, duration, parallel, 4, 3072).BuildAsync();

            foreach (var resolution in new[] { 512, 1024, 2048, 4096, 16384, 1 << 30 })
            {
                var request = new WaveformAnalysisRequest(MediaTime.Zero, resolution,
                    (SAMPLE_COUNT + resolution - 1) / resolution);
                var waveform = parallel.ReadWaveform(request, false)!;
                var expected = new float[request.BucketCount * 2];
                for (var sample = 0; sample < SAMPLE_COUNT; sample++)
                {
                    var value = Sample(originSample + sample);
                    var bucket = sample / resolution;
                    expected[bucket * 2] = Math.Min(expected[bucket * 2], value);
                    expected[bucket * 2 + 1] = Math.Max(expected[bucket * 2 + 1], value);
                }
                Assert.Equal(expected, waveform.Peaks.ToArray());
                Assert.Equal(serial.ReadWaveform(request, false)!.Peaks.ToArray(), waveform.Peaks.ToArray());
            }
            foreach (var resolution in new[] { 512, 2048, 8192 })
            {
                var request = new WaveformAnalysisRequest(MediaTime.Zero, resolution,
                    (SAMPLE_COUNT + resolution - 1) / resolution);
                var spectrum = parallel.ReadSpectrum(request, false)!;
                var firstCenter = mapping.ToMediaTime(spectrum.Start + spectrum.ColumnDuration / 2)
                    .ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
                float Read(long sample) => sample < originSample || sample >= originSample + SAMPLE_COUNT ? 0 : Sample(sample);
                var stride = checked((int)spectrum.ColumnDuration.ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE),
                    MediaTimeRounding.FLOOR).Value);
                var expected = AudioSpectrumWindowAnalyzer.Analyze(Read, static _ => { }, mapping.Origin,
                    firstCenter, stride, spectrum.Width, originSample, () => originSample + SAMPLE_COUNT, static () => { });
                Assert.Equal(expected.Levels.ToArray(), spectrum.Levels.ToArray());
                Assert.Equal(serial.ReadSpectrum(request, false)!.Levels.ToArray(), spectrum.Levels.ToArray());
            }
            Assert.True(serial.IsComplete);
            Assert.True(parallel.IsComplete);
            Assert.Equal(1, serialSource.SeekCount);
            Assert.Equal(1, parallelSource.SeekCount);
            Assert.Equal(SAMPLE_COUNT, serialSource.FramesRead);
            Assert.Equal(SAMPLE_COUNT, parallelSource.FramesRead);
            Assert.Equal(1, serialSource.DisposeCount);
            Assert.Equal(1, parallelSource.DisposeCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReopenedCompleteCacheSkipsTheDecoderFactory()
    {
        var directory = TemporaryDirectory();
        var duration = new MediaTime(1024, WaveformAnalyzer.SAMPLE_RATE);
        try
        {
            using (var writer = new AudioAnalysisCacheStore(directory, "reuse", new(MediaTime.Zero), duration))
            {
                await new AudioAnalysisCacheBuilder(_ => new WindowAudioSource(_ => 0.25F, 1024),
                    new(MediaTime.Zero), duration, writer, 1, 3072).BuildAsync();
            }
            using var reader = new AudioAnalysisCacheStore(directory, "reuse", new(MediaTime.Zero), duration);
            await new AudioAnalysisCacheBuilder(_ => throw new InvalidOperationException("完整缓存不得打开解码源。"),
                new(MediaTime.Zero), duration, reader, 1, 3072).BuildAsync();
            Assert.True(reader.IsComplete);
            var expected = new[] { 0F, 0.25F, 0F, 0.25F };
            Assert.Equal(expected, reader.ReadWaveform(new(MediaTime.Zero, 512, 2), false)!.Peaks.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BatchCheckpointPublishesCompletedBucketsAndCancellationDisposesTheOnlyDecoder()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var store = new AudioAnalysisCacheStore(directory, "cancel", new(MediaTime.Zero), new(10));
            var source = new WindowAudioSource(_ => 0.25F, 10L * WaveformAnalyzer.SAMPLE_RATE);
            using var cancellation = new CancellationTokenSource();
            var checkpoints = 0;
            var builder = new AudioAnalysisCacheBuilder(_ => source, new(MediaTime.Zero), new(10), store, 2, 3072);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildAsync(_ =>
            {
                checkpoints++;
                Assert.Equal(new MediaTime(6144, WaveformAnalyzer.SAMPLE_RATE), store.AvailableDuration);
                Assert.Equal(12, store.ReadWaveform(new(MediaTime.Zero, 512, 20), true)!.BucketCount);
                Assert.Null(store.ReadWaveform(new(MediaTime.Zero, 8192, 1), false));
                cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellationToken: cancellation.Token));
            Assert.Equal(1, checkpoints);
            Assert.False(store.IsComplete);
            Assert.Equal(1, source.SeekCount);
            Assert.Equal(1, source.CancelCount);
            Assert.Equal(1, source.DisposeCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RealTimestampGapAndEarlyEofRemainSilentAfterEveryLodIsSealed()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var store = new AudioAnalysisCacheStore(directory, "gap", new(MediaTime.Zero), new(7200));
            var source = new WindowAudioSource(_ => 0.5F, 8193)
            {
                Gap = (2048, 4096)
            };
            await new AudioAnalysisCacheBuilder(_ => source, new(MediaTime.Zero), new(7200), store, 4, 3072).BuildAsync();

            Assert.Equal(8193, store.ConfirmedSourceEnd);
            var gap = store.ReadWaveform(new(new(2048, WaveformAnalyzer.SAMPLE_RATE), 512, 4), false)!;
            Assert.All(gap.Peaks.ToArray(), sample => Assert.Equal(0, sample));
            var later = store.ReadWaveform(new(new(3600), 512, 16), false)!;
            Assert.All(later.Peaks.ToArray(), sample => Assert.Equal(0, sample));
            Assert.Equal(1, source.SeekCount);
            Assert.Equal(6145, source.FramesRead);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task InvalidPcmFailsBeforeCompletingTheCacheAndStillDisposesTheSource()
    {
        var directory = TemporaryDirectory();
        try
        {
            using var store = new AudioAnalysisCacheStore(directory, "invalid", new(MediaTime.Zero), new(1));
            var source = new WindowAudioSource(sample => sample == 1024 ? float.NaN : 0, WaveformAnalyzer.SAMPLE_RATE);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new AudioAnalysisCacheBuilder(_ => source, new(MediaTime.Zero), new(1), store, 1, 3072).BuildAsync());
            Assert.False(store.IsComplete);
            Assert.Equal(1, source.DisposeCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-builder-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
