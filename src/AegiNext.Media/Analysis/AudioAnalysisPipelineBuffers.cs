namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisPipelineBuffers
{
    internal AudioAnalysisPipelineBuffers(int workers, int segmentSamples, AudioAnalysisRecipe recipe)
    {
        Workers = workers;
        SegmentSamples = segmentSamples;
        RawSamples = Enumerable.Range(0, workers).Select(_ => new float[checked(segmentSamples + recipe.RawPadding * 2)]).ToArray();
        Processors = Enumerable.Range(0, workers).Select(_ => new AudioSpectrumSegmentProcessor(recipe)).ToArray();
        var columns = checked(segmentSamples / (recipe.HopSize * recipe.Decimation) + 1);
        Levels = Enumerable.Range(0, workers).Select(_ => new byte[checked(columns * recipe.FrequencyBins)]).ToArray();
        CoarseLevels = new byte[checked((columns + 1) / 2 * recipe.FrequencyBins)];
    }

    internal int Workers { get; }
    internal int SegmentSamples { get; }
    internal float[][] RawSamples { get; }
    internal AudioSpectrumSegmentProcessor[] Processors { get; }
    internal byte[][] Levels { get; }
    internal byte[] CoarseLevels { get; }
}
