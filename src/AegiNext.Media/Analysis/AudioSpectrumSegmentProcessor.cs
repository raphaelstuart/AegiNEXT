namespace AegiNext.Media.Analysis;

internal sealed class AudioSpectrumSegmentProcessor
{
    private readonly double[] power = new double[SpectrogramAnalyzer.FFT_SIZE / 2 + 1];
    private readonly SpectrumTransform transform = new(SpectrogramAnalyzer.FFT_SIZE);
    private float[] samples = [];

    internal byte[] Analyze(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter, int columns,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        var count = checked((columns - 1) * SpectrogramAnalyzer.HOP_SIZE + SpectrogramAnalyzer.FFT_SIZE);
        if (samples.Length < count)
        {
            samples = new float[count];
        }
        return AudioSpectrumWindowAnalyzer.AnalyzeSegment(rawSamples, rawFirstSample, firstCenter, columns,
            samples.AsSpan(0, count), power, transform, cancellationToken);
    }
}
