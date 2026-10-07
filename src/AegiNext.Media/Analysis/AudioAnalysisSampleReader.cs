namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisSampleReader(Func<long, AudioAnalysisTile> load, long mediaStart, long mediaEnd)
{
    internal const int TILE_SAMPLES = 65536;
    internal const int PADDING = SpectrogramAnalyzer.FFT_SIZE / 2 * 3 + AudioSpectrumWindowAnalyzer.FIR_HALF;
    private AudioAnalysisTile? current;
    private long firstSample;

    internal void Prepare(long sample)
    {
        var index = AudioSpectrumWindowAnalyzer.Floor(sample, TILE_SAMPLES) / TILE_SAMPLES;
        if (current?.Key.Index == index)
        {
            return;
        }
        current = load(index);
        firstSample = index * TILE_SAMPLES - PADDING;
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
}
