using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisSessionTests
{
    [Fact]
    public async Task OverviewYieldsToLatestViewportWithoutReopeningOrCancellingTheDecoder()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 20L)
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
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(20));
        var overview = session.GetOverviewAsync(new(MediaTime.Zero, 2048, 469), false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var detail = session.GetWindowAsync(new(new(10), 32, 100), false);
        release.Set();
        var result = await detail.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MediaTime(10), result.Waveform.Start);
        await overview.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaTime.Zero, source.SeekTargets[0]);
        Assert.True(source.SeekTargets[1] > new MediaTime(9));
        Assert.Equal(MediaTime.Zero, source.SeekTargets[2]);
        Assert.Equal(0, source.CancelCount);
    }

    [Fact]
    public async Task ClosingCancelsABlockedReadAndWaitsForDecoderDisposal()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 20L)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(20));
        var pending = session.GetWindowAsync(new(MediaTime.Zero, 32, 100), true);
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

    [Fact]
    public async Task PtsGapsAndEndPaddingRemainSilentRatherThanMovingLaterSamplesEarlier()
    {
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 3L, 317)
        {
            Gap = (WaveformAnalyzer.SAMPLE_RATE, WaveformAnalyzer.SAMPLE_RATE * 2L)
        };
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(5));
        var gap = await session.GetWindowAsync(new(new(5, 4), 32, 64), true);
        Assert.All(gap.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(gap.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
        var later = await session.GetWindowAsync(new(new(9, 4), 32, 64), false);
        Assert.Equal(0.5F, later.Waveform.Peaks.Span[1]);
        var padded = await session.GetWindowAsync(new(new(4), 32, 64), true);
        Assert.All(padded.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(padded.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
    }

    [Theory]
    [InlineData(600)]
    [InlineData(3600)]
    public async Task LongMediaKeepsTheImpulseInsideItsExactLocalSampleBucket(int duration)
    {
        const long IMPULSE_SAMPLE = 2885904;
        var source = new WindowAudioSource(index => index == IMPULSE_SAMPLE ? 0.8F : 0F,
            (long)duration * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(duration));
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
        Assert.InRange(session.CachedBytes, 1, 64L * 1024 * 1024);
    }

    [Fact]
    public async Task AdjacentRequestsReuseTilesAndEvictionDoesNotInvalidateDeliveredData()
    {
        var source = new WindowAudioSource(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 5L);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(5), maximumCachedBytes: 8192);
        var first = await session.GetWindowAsync(new(MediaTime.Zero, 1, 16), false);
        var seeks = source.SeekCount;
        await session.GetWindowAsync(new(new(8, WaveformAnalyzer.SAMPLE_RATE), 1, 16), false);
        Assert.Equal(seeks, source.SeekCount);
        await session.GetWindowAsync(new(new(1), 1, 16), false);
        Assert.True(source.SeekCount > seeks);
        Assert.Equal(8192, session.CachedBytes);
        Assert.Equal(0.5F, first.Waveform.Peaks.Span[1]);
        var afterEviction = source.SeekCount;
        await session.GetWindowAsync(new(MediaTime.Zero, 1, 16), false);
        Assert.True(source.SeekCount > afterEviction);
    }

    [Theory]
    [InlineData(192000)]
    [InlineData(-192000)]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task FrequencyWindowsUseOneMediaHopGridAcrossTilesChunkingAndOrigins(long originSamples)
    {
        var origin = new MediaTime(originSamples, WaveformAnalyzer.SAMPLE_RATE);
        var end = originSamples + 50L * WaveformAnalyzer.SAMPLE_RATE;
        var firstSample = originSamples;
        static float Sample(long index) => (float)(0.5 * Math.Sin(index * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
        await using var whole = new AudioAnalysisSession(_ => new WindowAudioSource(Sample, end, firstSample: firstSample), new(origin), new(50));
        await using var split = new AudioAnalysisSession(_ => new WindowAudioSource(Sample, end, 317, firstSample), new(origin), new(50));
        var full = (await whole.GetWindowAsync(new(new(12), 1024, 1400), true)).Spectrogram!;
        var local = (await split.GetWindowAsync(new(new(16), 1024, 700), true)).Spectrogram!;
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

    [Fact]
    public async Task SupersedingWorkKeepsTheDecoderUsableAndClosingDrainsIt()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        var source = new WindowAudioSource(_ => 0.25F, WaveformAnalyzer.SAMPLE_RATE * 20L)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        await using var session = new AudioAnalysisSession(_ =>
        {
            Interlocked.Increment(ref opened);
            return source;
        }, new(MediaTime.Zero), new(20));
        var old = session.GetWindowAsync(new(new(1), 32, 100), false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = session.GetWindowAsync(new(new(10), 32, 100), false);
        release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        var result = await latest.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MediaTime(10), result.Waveform.Start);
        Assert.Equal(1, opened);
        Assert.Equal(0, source.CancelCount);
        await session.DisposeAsync();
        Assert.Equal(1, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
    }
}
