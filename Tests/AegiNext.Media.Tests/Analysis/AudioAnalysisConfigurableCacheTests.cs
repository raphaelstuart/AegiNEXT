using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisConfigurableCacheTests
{
    [Theory]
    [InlineData(8000, 512, 2, 64, AudioSpectrumWindow.HANN)]
    [InlineData(16000, 1024, 4, 128, AudioSpectrumWindow.HAMMING)]
    [InlineData(24000, 2048, 8, 256, AudioSpectrumWindow.BLACKMAN)]
    [InlineData(48000, 4096, 4, 512, AudioSpectrumWindow.HANN)]
    public async Task ConfiguredSpectrumKeepsToneFrequencyAndAbsoluteTimeGrid(int rate, int fftSize, int divisor,
        int rows, AudioSpectrumWindow window)
    {
        var recipe = new AudioAnalysisRecipe
        {
            SpectrumSampleRate = rate, FftSize = fftSize, HopDivisor = divisor,
            FrequencyBins = rows, Window = window, MaximumFrequency = rate / 2.0
        };
        var origin = new MediaTime(1, 50);
        var source = new WindowAudioSource(index => (float)(0.4 * Math.Sin(index * 2 * Math.PI * 1000 / 48000)),
            24000, firstSample: 960);
        await using var session = new AudioAnalysisSession(_ => source, new(origin), new(2, 5),
            options: new() { Recipe = recipe, Execution = new() { MaximumWorkers = 1 } });
        await session.PrepareCacheAsync();
        var spectrum = (await session.GetWindowAsync(new(new(1, 10), 512, 4), true)).Spectrogram!;
        Assert.Equal(rows, spectrum.Height);
        Assert.Equal(new MediaTime(recipe.HopSize, rate), spectrum.ColumnDuration);
        var center = spectrum.Start + spectrum.ColumnDuration / 2 + origin;
        Assert.Equal(0, center.ToTimestamp(new(1, rate), MediaTimeRounding.TO_EVEN).Value % recipe.HopSize);
        var column = spectrum.Width / 2;
        var levels = Enumerable.Range(0, rows).Select(row => spectrum.Levels.Span[row * spectrum.Width + column]).ToArray();
        var expectedRow = (int)(Math.Log(1000 / recipe.MinimumFrequency) /
                                Math.Log(recipe.MaximumFrequency / recipe.MinimumFrequency) * rows);
        var peakRow = Array.IndexOf(levels, levels.Max());
        Assert.InRange(peakRow, expectedRow - 3, expectedRow + 3);
        Assert.True(levels.Max() > 100);
    }

    [Fact]
    public async Task ExecutionChangesReuseCacheWhileAnalysisChangesCreateAnotherIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        try
        {
            string path;
            await using (var first = new AudioAnalysisSession(_ => new WindowAudioSource(_ => 0.4F, 4800),
                             new(MediaTime.Zero), new(1, 10), cacheDirectory: directory, cacheIdentity: "options",
                             options: new()))
            {
                await first.PrepareCacheAsync();
                path = first.CacheDirectory;
            }
            await using var reused = new AudioAnalysisSession(_ => throw new InvalidOperationException("Cache must be reused"),
                new(MediaTime.Zero), new(1, 10), cacheDirectory: directory, cacheIdentity: "options",
                options: new() { Execution = new() { MaximumWorkers = 1, MemoryBudgetMiB = 32, SegmentSamples = 49152 } });
            Assert.Equal(path, reused.CacheDirectory);
            Assert.True(reused.IsCacheComplete);
            await reused.PrepareCacheAsync();
            await using var changed = new AudioAnalysisSession(_ => new WindowAudioSource(_ => 0.4F, 4800),
                new(MediaTime.Zero), new(1, 10), cacheDirectory: directory, cacheIdentity: "options",
                options: new() { Recipe = new() { FftSize = 2048 } });
            Assert.NotEqual(path, changed.CacheDirectory);
            Assert.False(changed.IsCacheComplete);
            await changed.PrepareCacheAsync();
            Assert.NotNull((await reused.GetWindowAsync(new(MediaTime.Zero, 512, 1), true)).Spectrogram);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FinerPersistentWaveformBucketsDoNotOpenTheDetailDecoder()
    {
        await using var session = new AudioAnalysisSession(_ => new WindowAudioSource(index => index == 123 ? 0.8F : 0, 4096),
            new(MediaTime.Zero), new(4096, 48000), options: new() { Recipe = new() { WaveformBaseSamples = 128 } },
            detailSourceFactory: _ => throw new InvalidOperationException("Persistent bucket should not decode locally"));
        await session.PrepareCacheAsync();
        var result = await session.GetWindowAsync(new(MediaTime.Zero, 128, 8), false);
        Assert.Equal(0.8F, result.Waveform.Peaks.Span[1]);
        Assert.All(result.Waveform.Peaks.ToArray().Skip(2), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task WiderAnalysisDbRangeRecoversWeakTonesRatherThanOnlyBrighteningClippedPixels()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new AudioAnalysisOptions();
            await using var original = new AudioAnalysisSession(_ => new WindowAudioSource(Tone, 9600),
                new(MediaTime.Zero), new(1, 5), cacheDirectory: directory, cacheIdentity: "weak-tone", options: options);
            await using var expanded = new AudioAnalysisSession(_ => new WindowAudioSource(Tone, 9600),
                new(MediaTime.Zero), new(1, 5), cacheDirectory: directory, cacheIdentity: "weak-tone",
                options: options with { Recipe = options.Recipe with { MinimumDecibels = -120 } });
            await original.PrepareCacheAsync();
            await expanded.PrepareCacheAsync();
            var request = new WaveformAnalysisRequest(new(1, 10), 512, 4);
            var dark = (await original.GetWindowAsync(request, true)).Spectrogram!;
            var visible = (await expanded.GetWindowAsync(request, true)).Spectrogram!;
            Assert.All(dark.Levels.ToArray(), value => Assert.Equal(0, value));
            Assert.Contains(visible.Levels.ToArray(), value => value > 0);
            Assert.NotEqual(original.CacheDirectory, expanded.CacheDirectory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static float Tone(long index)
    {
        return (float)(0.00003 * Math.Sin(index * 2 * Math.PI * 1000 / 48000));
    }
}
