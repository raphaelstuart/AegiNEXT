using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Analysis;

public sealed class SpectrogramAnalyzerTests
{
    [Fact]
    public void ToneAppearsAtTheCorrectLogFrequencyAndChunkingDoesNotChangeTheResult()
    {
        var samples = Tone(16000, 1000);
        using var single = new AnalysisAudioSource([new(new(16000, 1), MediaTime.Zero, samples)]);
        using var chunked = new AnalysisAudioSource(Chunk(samples, 317));
        var first = SpectrogramAnalyzer.Analyze(single, MediaTime.Zero, new(1));
        var second = SpectrogramAnalyzer.Analyze(chunked, MediaTime.Zero, new(1));
        Assert.Equal(first.Levels.ToArray(), second.Levels.ToArray());
        Assert.Equal(first.Waveform.ToArray(), second.Waveform.ToArray());
        var strongestRow = Enumerable.Range(0, first.Height)
            .MaxBy(row => Enumerable.Range(0, first.Width).Sum(column => first.Levels.Span[row * first.Width + column]));
        var expectedRow = (int)(Math.Log(1000.0 / 40) / Math.Log(8000.0 / 40) * 128);
        Assert.InRange(strongestRow, expectedRow - 1, expectedRow + 1);
        Assert.Contains(first.Levels.ToArray(), level => level > 180);
        Assert.InRange(first.Waveform.Span[20], -0.51F, -0.49F);
        Assert.InRange(first.Waveform.Span[21], 0.49F, 0.51F);
        Assert.Equal(0, single.DisposeCount);
    }

    [Fact]
    public void SilenceAndTimestampGapsRemainBlankAndOriginSelectsProjectRelativeTime()
    {
        using var silent = new AnalysisAudioSource([new(new(16000, 1), new(4), new float[16000])]);
        var silence = SpectrogramAnalyzer.Analyze(silent, new(4), new(1));
        Assert.All(silence.Levels.ToArray(), level => Assert.Equal(0, level));
        Assert.All(silence.Waveform.ToArray(), sample => Assert.Equal(0, sample));

        var tone = Tone(3200, 500);
        using var source = new AnalysisAudioSource([
            new(new(16000, 1), new(4), tone),
            new(new(16000, 1), new(48, 10), tone)]);
        var data = SpectrogramAnalyzer.Analyze(source, new(4), new(1));
        for (var column = data.Width / 3; column < data.Width * 2 / 3; column++)
        {
            Assert.Equal(0, data.Waveform.Span[column * 2]);
            Assert.Equal(0, data.Waveform.Span[column * 2 + 1]);
            for (var row = 0; row < data.Height; row++)
            {
                Assert.Equal(0, data.Levels.Span[row * data.Width + column]);
            }
        }

        Assert.True(data.Waveform.Span[1] > 0.4);
        Assert.True(data.Waveform.Span[^1] > 0.4);
    }

    [Fact]
    public void CacheIsBoundedEvenForSixHoursAndOwnsItsStorage()
    {
        using var source = new AnalysisAudioSource([]);
        var data = SpectrogramAnalyzer.Analyze(source, MediaTime.Zero, new(21600));
        Assert.Equal(4096, data.Width);
        Assert.Equal(4096 * 128, data.Levels.Length);
        Assert.Equal(8192, data.Waveform.Length);
        byte[] levels = [127];
        float[] waveform = [-0.5F, 0.5F];
        var copied = new SpectrogramData(1, 1, new(1), levels, waveform);
        levels[0] = 0;
        waveform[1] = 0;
        Assert.Equal(127, copied.Levels.Span[0]);
        Assert.Equal(0.5F, copied.Waveform.Span[1]);
    }

    [Fact]
    public void InvalidSamplesFormatsAndRangesAreRejected()
    {
        using var source = new AnalysisAudioSource([]);
        Assert.Throws<ArgumentException>(() => SpectrogramAnalyzer.Analyze(source, MediaTime.Zero, MediaTime.Zero));
        Assert.Throws<ArgumentException>(() => SpectrogramAnalyzer.Analyze(source, MediaTime.Zero, new(21601)));
        using var stereo = new AnalysisAudioSource([]) { Format = new(16000) };
        Assert.Throws<ArgumentException>(() => SpectrogramAnalyzer.Analyze(stereo, MediaTime.Zero, new(1)));
        using var nonFinite = new AnalysisAudioSource([new(new(16000, 1), MediaTime.Zero, [float.NaN])]);
        Assert.Throws<InvalidDataException>(() => SpectrogramAnalyzer.Analyze(nonFinite, MediaTime.Zero, new(1)));
        using var wrongBlock = new AnalysisAudioSource([new(new(), MediaTime.Zero, [0F, 0F])]);
        Assert.Throws<InvalidDataException>(() => SpectrogramAnalyzer.Analyze(wrongBlock, MediaTime.Zero, new(1)));
        using var backwards = new AnalysisAudioSource([
            new(new(16000, 1), new(1, 2), new float[1600]),
            new(new(16000, 1), MediaTime.Zero, new float[1600])]);
        Assert.Throws<InvalidDataException>(() => SpectrogramAnalyzer.Analyze(backwards, MediaTime.Zero, new(1)));
    }

    [Fact]
    public void CancellationDoesNotReturnPartialResultsOrTakeOwnershipOfTheSource()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new AnalysisAudioSource([new(new(16000, 1), MediaTime.Zero, Tone(1600, 1000))])
        {
            BeforeRead = cancellation.Cancel
        };
        Assert.ThrowsAny<OperationCanceledException>(() => SpectrogramAnalyzer.Analyze(source, MediaTime.Zero, new(1), cancellation.Token));
        Assert.Equal(0, source.DisposeCount);
        Assert.ThrowsAny<OperationCanceledException>(() => SpectrogramAnalyzer.Analyze(source, MediaTime.Zero, new(1), cancellation.Token));
    }

    private static float[] Tone(int count, double frequency)
    {
        return Enumerable.Range(0, count).Select(index => (float)(0.5 * Math.Sin(index * 2 * Math.PI * frequency / 16000))).ToArray();
    }

    private static IEnumerable<AudioSampleBlock> Chunk(float[] samples, int size)
    {
        for (var offset = 0; offset < samples.Length; offset += size)
        {
            yield return new(new(16000, 1), new(offset, 16000), samples.AsSpan(offset, Math.Min(size, samples.Length - offset)));
        }
    }
}
