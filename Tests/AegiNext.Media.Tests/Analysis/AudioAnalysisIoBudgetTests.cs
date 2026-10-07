using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Xunit.Abstractions;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisIoBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SpectrumOnlyClipsFftCentersImmediatelyWhenTheFirstPcmTileConfirmsEarlyEof()
    {
        var source = new WindowAudioSource(_ => 0.25F, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(7200));
        var result = await session.GetLayersAsync(new(MediaTime.Zero, 512, 128), false, true);
        Assert.Null(result.Waveform);
        Assert.NotNull(result.Spectrogram);
        Assert.Equal(new MediaTime(256, SpectrogramAnalyzer.SAMPLE_RATE), result.Spectrogram.ColumnDuration);
        for (var column = 0; column < result.Spectrogram.Width; column++)
        {
            var center = result.Spectrogram.Start + result.Spectrogram.ColumnDuration * column + result.Spectrogram.ColumnDuration / 2;
            if (center >= new MediaTime(1))
            {
                for (var row = 0; row < result.Spectrogram.Height; row++)
                {
                    Assert.Equal(0, result.Spectrogram.Levels.Span[row * result.Spectrogram.Width + column]);
                }
            }
        }
        var reads = source.ReadCount;
        var seeks = source.SeekCount;
        await session.GetLayersAsync(new(new(3600), 512, 128), false, true);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(seeks, source.SeekCount);
    }

    [Fact]
    public async Task AdjacentUncachedWindowsContinueThePcmCursorAndBackwardWindowsReusePeakSummaries()
    {
        var source = new WindowAudioSource(_ => 0.25F, 30L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(30), maximumCachedBytes: 8192);
        await session.GetWindowAsync(new(MediaTime.Zero, 65536, 1), false);
        var second = await session.GetWindowAsync(new(new(65536, WaveformAnalyzer.SAMPLE_RATE), 65536, 1), false);

        Assert.Equal(0.25F, second.Waveform.Peaks.Span[1]);
        Assert.Equal(1, source.SeekCount);
        var reads = source.ReadCount;
        var frames = source.FramesRead;
        var backward = await session.GetWindowAsync(new(MediaTime.Zero, 32768, 1), false);
        Assert.Equal(0.25F, backward.Waveform.Peaks.Span[1]);
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(frames, source.FramesRead);
        Assert.Equal(0, source.CancelCount);
        Assert.InRange(session.CachedBytes, 1, 8192);
    }

    [Fact]
    public async Task AContinuousTenMinuteViewportUsesSequentialDecodeRatherThanSeekingForEachTimeTile()
    {
        var source = new WindowAudioSource(_ => 0.25F, 7800L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(7800));
        var request = new WaveformAnalysisRequest(new(100), 16384, 1758);

        var result = await session.GetWindowAsync(request, false);

        output.WriteLine($"600s viewport: frames={source.FramesRead}, reads={source.ReadCount}, seeks={source.SeekCount}");
        Assert.InRange(source.SeekCount, 1, 2);
        Assert.InRange(source.FramesRead, 600L * WaveformAnalyzer.SAMPLE_RATE, 604L * WaveformAnalyzer.SAMPLE_RATE);
        Assert.All(result.Waveform.Peaks.ToArray().Where((_, index) => index % 2 != 0), sample => Assert.Equal(0.25F, sample));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedEarlyEofStopsFurtherTilesAndUncachedDistantWindows(bool spectrum)
    {
        var source = new WindowAudioSource(_ => 0.25F, WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(7200));
        var first = await session.GetWindowAsync(new(MediaTime.Zero, 16384, 1758), spectrum);

        output.WriteLine($"Actual EOF 1s / declared 7200s / spectrum={spectrum}: frames={source.FramesRead}, reads={source.ReadCount}, seeks={source.SeekCount}");
        Assert.Equal(0.25F, first.Waveform.Peaks.Span[1]);
        Assert.InRange(source.SeekCount, 1, 2);
        Assert.InRange(source.ReadCount, 1, 14);
        Assert.Equal(WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        var seeks = source.SeekCount;
        var reads = source.ReadCount;
        var later = await session.GetWindowAsync(new(new(3600), 16384, 64), spectrum);

        Assert.Equal(seeks, source.SeekCount);
        Assert.Equal(reads, source.ReadCount);
        Assert.All(later.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        if (later.Spectrogram is { } spectrogram)
        {
            Assert.All(spectrogram.Levels.ToArray(), sample => Assert.Equal(0, sample));
        }
    }

    [Theory]
    [InlineData(65536)]
    [InlineData(131072)]
    public async Task CoarseWaveformBucketsAggregateSmallTimeTilesWithoutExpandingToTwentyMinutes(int samplesPerBucket)
    {
        var source = new WindowAudioSource(_ => 0.25F, 21600L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(21600));
        var request = new WaveformAnalysisRequest(new(100), samplesPerBucket, 1);

        var result = await session.GetWindowAsync(request, false);

        Assert.Equal(0.25F, result.Waveform.Peaks.Span[1]);
        Assert.InRange(source.FramesRead, 1, samplesPerBucket + 8192L);
        Assert.InRange(source.SeekCount, 1, 2);
    }

    [Fact]
    public async Task IdenticalViewportsHaveIdenticalIoAndRemainIdleAfterCompletionAcrossMediaDurations()
    {
        long? frames = null;
        int? seeks = null;
        foreach (var seconds in new[] { 30, 7800, 21600 })
        {
            var source = new WindowAudioSource(_ => 0.25F, (long)seconds * WaveformAnalyzer.SAMPLE_RATE);
            await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(seconds));
            await session.GetWindowAsync(new(new(10), 8192, 32), true);
            output.WriteLine($"duration={seconds}: frames={source.FramesRead}, reads={source.ReadCount}, seeks={source.SeekCount}");
            frames ??= source.FramesRead;
            seeks ??= source.SeekCount;
            Assert.Equal(frames.Value, source.FramesRead);
            Assert.Equal(seeks.Value, source.SeekCount);
            await Task.Delay(100);
            Assert.Equal(frames.Value, source.FramesRead);
            Assert.Equal(seeks.Value, source.SeekCount);
        }
    }

    [Fact]
    public async Task ActualEarlyEofAndRepeatedPaddedWindowsDoNotReadOrSeekAgain()
    {
        var source = new WindowAudioSource(_ => 0.5F, 3L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(21600));
        var request = new WaveformAnalysisRequest(new(4), 512, 128);
        var result = await session.GetWindowAsync(request, true);
        Assert.All(result.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(result.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
        var reads = source.ReadCount;
        var seeks = source.SeekCount;

        await session.GetWindowAsync(request, true);

        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(seeks, source.SeekCount);
    }

    [Fact]
    public async Task APostDurationPulseInTheLastDeliveredBlockCannotLeakIntoPeaksOrFft()
    {
        const long END_SAMPLE = 30L * WaveformAnalyzer.SAMPLE_RATE;
        var source = new WindowAudioSource(index => index >= END_SAMPLE ? 0.9F : 0, 40L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(30));

        var result = await session.GetWindowAsync(new(new(29), 512, 256), true);

        Assert.All(result.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(result.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
        Assert.True(source.LastReadSample <= END_SAMPLE + 4096);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(7800)]
    [InlineData(21600)]
    public async Task ALocalViewportReadsOnlyItsRangeAndBoundedTileEdgesRegardlessOfMediaDuration(int seconds)
    {
        var source = new WindowAudioSource(_ => 0.25F, (long)seconds * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(seconds));
        var request = new WaveformAnalysisRequest(new(10), 8192, 32);

        var result = await session.GetWindowAsync(request, true);

        output.WriteLine($"duration={seconds}: frames={source.FramesRead}, reads={source.ReadCount}, seeks={source.SeekCount}, cache={session.CachedBytes}");
        Assert.NotNull(result.Spectrogram);
        Assert.Empty(result.Spectrogram.Waveform.ToArray());
        Assert.InRange(source.FramesRead, 1, 8L * WaveformAnalyzer.SAMPLE_RATE);
        Assert.InRange(source.SeekCount, 1, 8);
        Assert.InRange(source.FirstReadSample!.Value, 8L * WaveformAnalyzer.SAMPLE_RATE, 10L * WaveformAnalyzer.SAMPLE_RATE);
        Assert.InRange(source.LastReadSample!.Value, 15L * WaveformAnalyzer.SAMPLE_RATE, 18L * WaveformAnalyzer.SAMPLE_RATE);
        var reads = source.ReadCount;
        var seeks = source.SeekCount;
        var cached = await session.GetWindowAsync(request, true);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(seeks, source.SeekCount);
        Assert.Equal(result.Waveform.Peaks.ToArray(), cached.Waveform.Peaks.ToArray());
        Assert.Equal(result.Spectrogram.Levels.ToArray(), cached.Spectrogram!.Levels.ToArray());
    }

    [Fact]
    public async Task CoarseSpectrumDoesNotSeekForEachColumnOrReadAFixed1024ColumnTile()
    {
        var source = new WindowAudioSource(_ => 0.25F, 21600L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(21600));
        var request = new WaveformAnalysisRequest(new(100), 8192, 12);

        var result = await session.GetWindowAsync(request, true);

        Assert.NotNull(result.Spectrogram);
        Assert.InRange(source.SeekCount, 1, 4);
        Assert.InRange(source.FramesRead, 1, 5L * WaveformAnalyzer.SAMPLE_RATE);
        Assert.True(source.FirstReadSample >= 98L * WaveformAnalyzer.SAMPLE_RATE);
        Assert.True(source.LastReadSample <= 105L * WaveformAnalyzer.SAMPLE_RATE);
    }

    [Fact]
    public async Task CombinedWaveformAndSpectrumReuseTheSameDecodedPcm()
    {
        var waveformSource = new WindowAudioSource(_ => 0.25F, 21600L * WaveformAnalyzer.SAMPLE_RATE);
        var combinedSource = new WindowAudioSource(_ => 0.25F, 21600L * WaveformAnalyzer.SAMPLE_RATE);
        await using var waveform = new AudioAnalysisSession(_ => waveformSource, new(MediaTime.Zero), new(21600));
        await using var combined = new AudioAnalysisSession(_ => combinedSource, new(MediaTime.Zero), new(21600));
        var request = new WaveformAnalysisRequest(new(10), 512, 512);

        var peaks = await waveform.GetWindowAsync(request, false);
        var both = await combined.GetWindowAsync(request, true);

        Assert.Equal(peaks.Waveform.Peaks.ToArray(), both.Waveform.Peaks.ToArray());
        Assert.InRange(combinedSource.FramesRead, waveformSource.FramesRead, waveformSource.FramesRead + 65536);
        var reads = combinedSource.ReadCount;
        await combined.GetWindowAsync(request, false);
        await combined.GetWindowAsync(request, true);
        Assert.Equal(reads, combinedSource.ReadCount);
        Assert.InRange(combined.CachedBytes, 1, 64L * 1024 * 1024);
    }

    [Fact]
    public async Task EofClipsTilesAndAWindowBeyondDurationDoesNotSeekOrRead()
    {
        var source = new WindowAudioSource(_ => 0.5F, 21600L * WaveformAnalyzer.SAMPLE_RATE);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(30));
        var edge = await session.GetWindowAsync(new(new(29), 8192, 32), true);

        Assert.True(source.LastReadSample <= 30L * WaveformAnalyzer.SAMPLE_RATE + 4096);
        Assert.All(source.SeekTargets, target => Assert.True(target < new MediaTime(30)));
        var reads = source.ReadCount;
        var seeks = source.SeekCount;
        var beyond = await session.GetWindowAsync(new(new(100), 8192, 32), true);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(seeks, source.SeekCount);
        Assert.All(beyond.Waveform.Peaks.ToArray(), sample => Assert.Equal(0, sample));
        Assert.All(beyond.Spectrogram!.Levels.ToArray(), sample => Assert.Equal(0, sample));
        var firstSilent = (int)(new MediaTime(30) - edge.Waveform.Start)
            .ToTimestamp(new(8192, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        Assert.All(edge.Waveform.Peaks.ToArray().Skip(firstSilent * 2), sample => Assert.Equal(0, sample));
    }
}
