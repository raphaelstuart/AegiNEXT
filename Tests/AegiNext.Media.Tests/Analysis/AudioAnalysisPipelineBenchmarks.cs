using System.Diagnostics;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Xunit.Abstractions;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisPipelineBenchmarks(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task CompareWindowFirWithReusableSerialAndParallelSegmentProcessors()
    {
        const int COLUMNS = 4096;
        const int SEGMENT_COLUMNS = 256;
        const long FIRST_SAMPLE = -AudioSpectrumWindowAnalyzer.RAW_PADDING;
        var count = (COLUMNS - 1) * SpectrogramAnalyzer.HOP_SIZE * 3 +
            SpectrogramAnalyzer.FFT_SIZE * 3 + AudioSpectrumWindowAnalyzer.FIR_HALF * 2;
        var samples = new float[count];
        for (var index = 0; index < samples.Length; index++)
        {
            var sample = FIRST_SAMPLE + index;
            samples[index] = (float)(0.35 * Math.Sin(sample * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE) +
                0.2 * Math.Sin(sample * 2 * Math.PI * 6000 / WaveformAnalyzer.SAMPLE_RATE));
        }
        var warmup = AudioSpectrumWindowAnalyzer.Analyze(sample => samples[(int)(sample - FIRST_SAMPLE)],
            static _ => { }, MediaTime.Zero, 0, SpectrogramAnalyzer.HOP_SIZE, SEGMENT_COLUMNS, FIRST_SAMPLE,
            () => FIRST_SAMPLE + count, static () => { });
        await AnalyzeSegmentsAsync(samples, FIRST_SAMPLE, SEGMENT_COLUMNS, 1);
        await AnalyzeSegmentsAsync(samples, FIRST_SAMPLE, SEGMENT_COLUMNS, 4);
        Assert.Contains(warmup.Levels.ToArray(), value => value > 100);
        output.WriteLine($"Input: {count:N0} float32 mono samples, 48 kHz, {COLUMNS * SpectrogramAnalyzer.HOP_SIZE / (double)SpectrogramAnalyzer.SAMPLE_RATE:F3} s, {COLUMNS} columns, {SEGMENT_COLUMNS} columns/segment.");
        output.WriteLine("Measures DSP only; input generation, media decoding, and disk I/O are excluded. Allocation uses process-wide GC.GetTotalAllocatedBytes(true).");

        byte[]? expected = null;
        for (var iteration = 1; iteration <= 3; iteration++)
        {
            var allocated = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            var old = AudioSpectrumWindowAnalyzer.Analyze(sample => samples[(int)(sample - FIRST_SAMPLE)],
                static _ => { }, MediaTime.Zero, 0, SpectrogramAnalyzer.HOP_SIZE, COLUMNS, FIRST_SAMPLE,
                () => FIRST_SAMPLE + count, static () => { });
            watch.Stop();
            output.WriteLine($"Iteration {iteration}: old window FIR: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetTotalAllocatedBytes(true) - allocated:N0} allocated bytes.");
            expected ??= old.Levels.ToArray();
            Assert.Equal(expected, old.Levels.ToArray());

            foreach (var workers in new[] { 1, 4 })
            {
                allocated = GC.GetTotalAllocatedBytes(true);
                watch.Restart();
                var actual = await AnalyzeSegmentsAsync(samples, FIRST_SAMPLE, COLUMNS, workers);
                watch.Stop();
                output.WriteLine($"Iteration {iteration}: new segment FIR with reusable output, workers={workers}: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetTotalAllocatedBytes(true) - allocated:N0} allocated bytes.");
                Assert.Equal(expected, actual);
            }
        }
    }

    private static async Task<byte[]> AnalyzeSegmentsAsync(float[] samples, long firstSample, int columns, int workers)
    {
        const int SEGMENT_COLUMNS = 256;
        var result = new byte[columns * SpectrogramAnalyzer.FREQUENCY_BINS];
        await Task.WhenAll(Enumerable.Range(0, workers).Select(worker => Task.Run(() =>
        {
            var processor = new AudioSpectrumSegmentProcessor();
            var levels = new byte[SEGMENT_COLUMNS * SpectrogramAnalyzer.FREQUENCY_BINS];
            for (var first = worker * SEGMENT_COLUMNS; first < columns; first += workers * SEGMENT_COLUMNS)
            {
                var count = Math.Min(SEGMENT_COLUMNS, columns - first);
                processor.AnalyzeInto(samples, firstSample, (long)first * SpectrogramAnalyzer.HOP_SIZE,
                    count, levels.AsSpan(0, count * SpectrogramAnalyzer.FREQUENCY_BINS), CancellationToken.None);
                for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
                {
                    levels.AsSpan(row * count, count).CopyTo(result.AsSpan(row * columns + first, count));
                }
            }
        })));
        return result;
    }
}
