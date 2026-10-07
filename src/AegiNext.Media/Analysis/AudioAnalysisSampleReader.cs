namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisSampleReader(Func<long, AudioAnalysisTile> load, long mediaStart, long mediaEnd,
    int tileSamples = AudioAnalysisSampleReader.TILE_SAMPLES)
{
    internal const int TILE_SAMPLES = 65536;
    internal const int PREVIEW_TILE_SAMPLES = 4096;
    internal const int PADDING = SpectrogramAnalyzer.FFT_SIZE / 2 * 3 + AudioSpectrumWindowAnalyzer.FIR_HALF;
    private AudioAnalysisTile? current;
    private long firstSample;

    internal void Prepare(long sample)
    {
        var index = AudioSpectrumWindowAnalyzer.Floor(sample, tileSamples) / tileSamples;
        if (current?.Key.Index == index)
        {
            return;
        }
        current = load(index);
        firstSample = index * tileSamples - PADDING;
    }

    internal float Read(long sample)
    {
        if (sample < mediaStart || sample >= mediaEnd)
        {
            return 0;
        }
        if (current is null || sample < firstSample || sample >= firstSample + current.Samples.Length)
        {
            Prepare(sample);
        }
        return current!.Samples.Span[(int)(sample - firstSample)];
    }

    internal ReadOnlySpan<float> ReadSpan(long sample, int maximumCount)
    {
        Prepare(sample);
        var offset = checked((int)(sample - firstSample));
        var count = Math.Min(maximumCount, current!.Samples.Length - offset);
        return current.Samples.Span.Slice(offset, count);
    }
}
