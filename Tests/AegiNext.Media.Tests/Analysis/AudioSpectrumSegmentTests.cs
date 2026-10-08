using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioSpectrumSegmentTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(1008)]
    [InlineData(-1008)]
    public void SharedDecimationMatchesExistingFftWindowsAtAbsoluteMediaPhase(long mediaStart)
    {
        const int COLUMNS = 17;
        var firstCenter = Ceiling(mediaStart, SpectrogramAnalyzer.HOP_SIZE * 3L) * SpectrogramAnalyzer.HOP_SIZE;
        var mediaEnd = (firstCenter + (COLUMNS - 1L) * SpectrogramAnalyzer.HOP_SIZE) * 3 + 1;
        float Read(long sample) => sample < mediaStart || sample >= mediaEnd ? 0 : Tone(sample);
        var expected = AudioSpectrumWindowAnalyzer.Analyze(Read, static _ => { }, new(mediaStart, WaveformAnalyzer.SAMPLE_RATE),
            firstCenter, SpectrogramAnalyzer.HOP_SIZE, COLUMNS, mediaStart, () => mediaEnd, static () => { });

        var actual = AnalyzeSegment(Read, firstCenter, COLUMNS);

        Assert.Equal(expected.Levels.ToArray(), actual);
    }

    [Fact]
    public void SplittingSpectrumAtUnevenColumnCountsPreservesEveryFrequencyRow()
    {
        const int COLUMNS = 533;
        const long FIRST_CENTER = -SpectrogramAnalyzer.HOP_SIZE;
        var expected = AnalyzeSegment(Tone, FIRST_CENTER, COLUMNS);
        var actual = new byte[expected.Length];
        var first = 0;
        foreach (var count in new[] { 93, 257, 183 })
        {
            var part = AnalyzeSegment(Tone, FIRST_CENTER + (long)first * SpectrogramAnalyzer.HOP_SIZE, count);
            for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
            {
                part.AsSpan(row * count, count).CopyTo(actual.AsSpan(row * COLUMNS + first, count));
            }
            first += count;
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ConcurrentSegmentsOwnTheirTransformBuffers()
    {
        const int COLUMNS = 19;
        var expected = Enumerable.Range(0, 4)
            .Select(index => AnalyzeSegment(Tone, (long)index * SpectrogramAnalyzer.HOP_SIZE, COLUMNS)).ToArray();

        var actual = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() =>
            AnalyzeSegment(Tone, (long)index * SpectrogramAnalyzer.HOP_SIZE, COLUMNS))));

        for (var index = 0; index < actual.Length; index++)
        {
            Assert.Equal(expected[index], actual[index]);
        }
    }

    [Fact]
    public void CancellationIsCheckedBeforeSegmentAnalysis()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => AudioSpectrumWindowAnalyzer.AnalyzeSegment(
            new float[4096], -AudioSpectrumWindowAnalyzer.RAW_PADDING, 0, 1, new(true)));
    }

    private static byte[] AnalyzeSegment(Func<long, float> read, long firstCenter, int columns)
    {
        var first = firstCenter * 3 - AudioSpectrumWindowAnalyzer.RAW_PADDING;
        var count = checked((columns - 1) * SpectrogramAnalyzer.HOP_SIZE * 3 +
            SpectrogramAnalyzer.FFT_SIZE * 3 + AudioSpectrumWindowAnalyzer.FIR_HALF * 2);
        var samples = new float[count];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = read(first + index);
        }
        return AudioSpectrumWindowAnalyzer.AnalyzeSegment(samples, first, firstCenter, columns, CancellationToken.None);
    }

    private static long Ceiling(long value, long step)
    {
        return -AudioSpectrumWindowAnalyzer.Floor(-value, step) / step;
    }

    private static float Tone(long sample)
    {
        return (float)(0.35 * Math.Sin(sample * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE) +
            0.2 * Math.Sin(sample * 2 * Math.PI * 6000 / WaveformAnalyzer.SAMPLE_RATE) +
            0.1 * Math.Sin(sample * 2 * Math.PI * 15000 / WaveformAnalyzer.SAMPLE_RATE));
    }
}
