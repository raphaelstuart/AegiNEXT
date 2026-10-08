namespace AegiNext.Media.Analysis;

internal sealed class AudioSpectrumSegmentProcessor
{
    private readonly AudioSpectrumAnalysisPlan plan;
    private readonly double[] power;
    private readonly double[] rowPower;
    private readonly SpectrumTransform transform;
    private float[] samples = [];

    internal AudioSpectrumSegmentProcessor(AudioAnalysisRecipe? recipe = null)
    {
        recipe ??= new();
        plan = new(recipe);
        power = new double[recipe.FftSize / 2 + 1];
        rowPower = new double[recipe.FrequencyBins];
        transform = new(recipe.FftSize, recipe.Window);
    }

    internal byte[] Analyze(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter, int columns,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        var levels = new byte[checked(columns * plan.Recipe.FrequencyBins)];
        AnalyzeInto(rawSamples, rawFirstSample, firstCenter, columns, levels, cancellationToken);
        return levels;
    }

    internal void AnalyzeInto(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter, int columns,
        Span<byte> levels, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        var count = checked((columns - 1) * plan.Recipe.HopSize + plan.Recipe.FftSize);
        if (samples.Length < count)
        {
            samples = new float[count];
        }
        AudioSpectrumWindowAnalyzer.AnalyzeSegmentInto(rawSamples, rawFirstSample, firstCenter, columns,
            samples.AsSpan(0, count), power, transform, plan, rowPower, levels, cancellationToken);
    }
}
