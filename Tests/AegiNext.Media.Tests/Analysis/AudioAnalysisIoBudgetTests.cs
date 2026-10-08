using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Xunit.Abstractions;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisIoBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SpectrumCacheClipsCentersAtActualEofAndDistantWindowsDoNotDecodeAgain()
    {
        var source = new WindowAudioSource(_ => 0.25F, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(7200));
        await session.PrepareCacheAsync();

        var result = await session.GetLayersAsync(new(MediaTime.Zero, 512, 128), false, true);

        Assert.Null(result.Waveform);
        var spectrum = Assert.IsType<SpectrogramData>(result.Spectrogram);
        Assert.Equal(new MediaTime(256, SpectrogramAnalyzer.SAMPLE_RATE), spectrum.ColumnDuration);
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center >= new MediaTime(1))
            {
                for (var row = 0; row < spectrum.Height; row++)
                {
                    Assert.Equal(0, spectrum.Levels.Span[row * spectrum.Width + column]);
                }
            }
        }
        var reads = source.ReadCount;
        var seeks = source.SeekCount;
        await session.GetLayersAsync(new(new(3600), 512, 128), false, true);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(seeks, source.SeekCount);
        Assert.Equal(WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        Assert.Equal(1, seeks);
    }

    [Fact]
    public async Task AdjacentAndBackwardCachedWindowsDoNotMoveTheSequentialScanCursor()
    {
        var source = new WindowAudioSource(_ => 0.25F, 4L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(4), maximumCachedBytes: 8192);
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var frames = source.FramesRead;

        await session.GetWindowAsync(new(MediaTime.Zero, 65536, 1), false);
        var adjacent = await session.GetWindowAsync(new(new(65536, WaveformAnalyzer.SAMPLE_RATE), 65536, 1), false);
        var backward = await session.GetWindowAsync(new(MediaTime.Zero, 32768, 1), false);

        Assert.Equal(0.25F, adjacent.Waveform.Peaks.Span[1]);
        Assert.Equal(0.25F, backward.Waveform.Peaks.Span[1]);
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(0, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.InRange(session.CachedBytes, 0, 8192);
    }

    [Fact]
    public async Task LongDeclaredMediaScansActualFramesOnceRatherThanSeekingPerViewport()
    {
        const int ACTUAL_SECONDS = 12;
        var source = new WindowAudioSource(_ => 0.25F, ACTUAL_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE, 317);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(7800));
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;

        var distant = await session.GetWindowAsync(new(new(100), 16384, 1758), true);

        output.WriteLine($"actual={ACTUAL_SECONDS}s, declared=7800s: frames={source.FramesRead}, reads={reads}, seeks={source.SeekCount}");
        Assert.True(session.IsCacheComplete);
        Assert.Equal(ACTUAL_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(reads, source.ReadCount);
        Assert.All(distant.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(distant.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedEarlyEofCompletesTheWholeCacheAndPadsDistantWindows(bool spectrum)
    {
        var source = new WindowAudioSource(_ => 0.25F, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(7200));
        await session.PrepareCacheAsync();
        var first = await session.GetWindowAsync(new(MediaTime.Zero, 16384, 1758), spectrum);
        Assert.Equal(0.25F, first.Waveform.Peaks.Span[1]);
        Assert.Equal(1, source.SeekCount);
        Assert.InRange(source.ReadCount, 1, 14);
        Assert.Equal(WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        var seeks = source.SeekCount;
        var reads = source.ReadCount;

        var later = await session.GetWindowAsync(new(new(3600), 16384, 64), spectrum);

        Assert.Equal(seeks, source.SeekCount);
        Assert.Equal(reads, source.ReadCount);
        Assert.All(later.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        if (later.Spectrogram is { } levels)
        {
            Assert.All(levels.Levels.ToArray(), value => Assert.Equal(0, value));
        }
    }

    [Theory]
    [InlineData(65536)]
    [InlineData(131072)]
    public async Task CoarseBucketsReadTheCacheWithoutExpandingARequestIntoDecodeWork(int samplesPerBucket)
    {
        var source = new WindowAudioSource(_ => 0.25F, 3L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(21600));
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;

        var result = await session.GetWindowAsync(new(new(1), samplesPerBucket, 1), false);

        Assert.Equal(0.25F, result.Waveform.Peaks.Span[1]);
        Assert.Equal(3L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
    }

    [Fact]
    public async Task CompletedCachesUseTheSameActualDecodeBudgetAcrossDeclaredDurations()
    {
        foreach (var seconds in new[] { 30, 7800, 21600 })
        {
            var source = new WindowAudioSource(_ => 0.25F, 2L * WaveformAnalyzer.SAMPLE_RATE);
            await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(seconds));
            await session.PrepareCacheAsync();
            var reads = source.ReadCount;
            await session.GetWindowAsync(new(new(1), 512, 32), true);
            await session.GetWindowAsync(new(new(10), 8192, 32), true);
            output.WriteLine($"declared={seconds}: frames={source.FramesRead}, reads={reads}, seeks={source.SeekCount}");
            Assert.Equal(2L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
            Assert.Equal(1, source.SeekCount);
            Assert.Equal(reads, source.ReadCount);
            Assert.Equal(1, source.DisposeCount);
        }
    }

    [Fact]
    public async Task RepeatedPaddedWindowsPastActualEofDoNotReadOrSeekAgain()
    {
        var source = new WindowAudioSource(_ => 0.5F, 3L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(21600));
        await session.PrepareCacheAsync();
        var request = new WaveformAnalysisRequest(new(4), 512, 128);
        var result = await session.GetWindowAsync(request, true);
        Assert.All(result.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(result.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        var reads = source.ReadCount;
        await session.GetWindowAsync(request, true);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
    }

    [Fact]
    public async Task PostDurationSamplesInTheLastBlockCannotLeakIntoCachedPeaksOrFft()
    {
        const long END_SAMPLE = 2L * WaveformAnalyzer.SAMPLE_RATE;
        var source = new WindowAudioSource(index => index >= END_SAMPLE ? 0.9F : 0, 3L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(2));
        var result = await session.GetWindowAsync(new(new(1), 512, 256), true);
        Assert.All(result.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(result.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        Assert.True(source.LastReadSample <= END_SAMPLE + 4096);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(7800)]
    [InlineData(21600)]
    public async Task FineWaveformOnlyReadsItsLocalRangeWithoutStartingTheWholeCache(int seconds)
    {
        var source = new WindowAudioSource(_ => 0.25F, (long)seconds * WaveformAnalyzer.SAMPLE_RATE);
        var opened = 0;
        await using var session = new AudioAnalysisSession(_ =>
        {
            Interlocked.Increment(ref opened);
            throw new InvalidOperationException("Fine waveform must not start the full scan.");
        }, new(MediaTime.Zero), new(seconds), detailSourceFactory: _ => source);
        var request = new WaveformAnalysisRequest(new(10), 32, 1024);

        var result = await session.GetWindowAsync(request, false);

        Assert.Null(result.Spectrogram);
        Assert.False(session.IsCacheComplete);
        Assert.Equal(0, opened);
        Assert.InRange(source.FramesRead, 1, 2L * 65536);
        Assert.Equal(1, source.SeekCount);
        Assert.InRange(source.FirstReadSample!.Value, 8L * WaveformAnalyzer.SAMPLE_RATE, 10L * WaveformAnalyzer.SAMPLE_RATE);
        Assert.InRange(source.LastReadSample!.Value, 10L * WaveformAnalyzer.SAMPLE_RATE, 12L * WaveformAnalyzer.SAMPLE_RATE);
        var reads = source.ReadCount;
        var cached = await session.GetWindowAsync(request, false);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(result.Waveform.Peaks.ToArray(), cached.Waveform.Peaks.ToArray());
    }

    [Fact]
    public async Task CoarseSpectrumUsesIndexedLevelsWithoutPerColumnDecoderAccess()
    {
        var source = new WindowAudioSource(_ => 0.25F, 4L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(21600));
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;

        var result = await session.GetWindowAsync(new(new(1), 8192, 12), true);

        Assert.NotNull(result.Spectrogram);
        Assert.Empty(result.Spectrogram.Waveform.ToArray());
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(4L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
    }

    [Fact]
    public async Task BothLayersShareOneScanAndOrdinaryZoomNeverOpensTheDetailDecoder()
    {
        const long FRAMES = 4L * WaveformAnalyzer.SAMPLE_RATE;
        var source = new WindowAudioSource(_ => 0.25F, FRAMES);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(4),
            detailSourceFactory: _ => throw new InvalidOperationException("Cached layers must not decode local PCM."));
        await session.PrepareCacheAsync();
        var reads = source.ReadCount;
        var request = new WaveformAnalysisRequest(new(1), 512, 128);
        var peaks = await session.GetWindowAsync(request, false);
        var both = await session.GetWindowAsync(request, true);
        Assert.Equal(peaks.Waveform.Peaks.ToArray(), both.Waveform.Peaks.ToArray());
        Assert.NotNull(both.Spectrogram);
        Assert.Equal(FRAMES, source.FramesRead);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
        Assert.InRange(session.CachedBytes, 0, 64L * 1024 * 1024);
    }

    [Fact]
    public async Task DeclaredDurationClipsTheScanAndLaterCachedWindowsStaySilent()
    {
        var source = new WindowAudioSource(_ => 0.5F, 10L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(3));
        await session.PrepareCacheAsync();
        var edge = await session.GetWindowAsync(new(new(2), 8192, 32), true);
        Assert.True(source.LastReadSample <= 3L * WaveformAnalyzer.SAMPLE_RATE + 4096);
        Assert.All(source.SeekTargets, target => Assert.True(target < new MediaTime(3)));
        var reads = source.ReadCount;
        var beyond = await session.GetWindowAsync(new(new(100), 8192, 32), true);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
        Assert.All(beyond.Waveform.Peaks.ToArray(), value => Assert.Equal(0, value));
        Assert.All(beyond.Spectrogram!.Levels.ToArray(), value => Assert.Equal(0, value));
        var firstSilent = (int)(new MediaTime(3) - edge.Waveform.Start)
            .ToTimestamp(new(8192, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        Assert.All(edge.Waveform.Peaks.ToArray().Skip(firstSilent * 2), value => Assert.Equal(0, value));
    }
}
