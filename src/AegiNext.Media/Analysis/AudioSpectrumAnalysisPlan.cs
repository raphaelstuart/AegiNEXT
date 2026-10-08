namespace AegiNext.Media.Analysis;

internal sealed class AudioSpectrumAnalysisPlan
{
    internal AudioAnalysisRecipe Recipe { get; }
    internal double[] Filter { get; }
    internal int[] Rows { get; }

    internal AudioSpectrumAnalysisPlan(AudioAnalysisRecipe recipe)
    {
        recipe.Validate();
        Recipe = recipe;
        Filter = new double[AudioSpectrumWindowAnalyzer.FIR_HALF * 2 + 1];
        var cutoff = 0.875 * recipe.SpectrumSampleRate / 2 / WaveformAnalyzer.SAMPLE_RATE;
        double sum = 0;
        for (var index = 0; index < Filter.Length; index++)
        {
            var position = index - AudioSpectrumWindowAnalyzer.FIR_HALF;
            var sinc = position == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * position) / (Math.PI * position);
            Filter[index] = sinc * (0.54 - 0.46 * Math.Cos(2 * Math.PI * index / (Filter.Length - 1)));
            sum += Filter[index];
        }
        for (var index = 0; index < Filter.Length; index++)
        {
            Filter[index] /= sum;
        }
        Rows = new int[recipe.FftSize / 2 + 1];
        var span = Math.Log(recipe.MaximumFrequency / recipe.MinimumFrequency);
        for (var bin = 1; bin < Rows.Length; bin++)
        {
            var frequency = bin * recipe.SpectrumSampleRate / (double)recipe.FftSize;
            Rows[bin] = frequency > recipe.MaximumFrequency ? -1 : Math.Clamp((int)(
                Math.Log(Math.Max(recipe.MinimumFrequency, frequency) / recipe.MinimumFrequency) / span * recipe.FrequencyBins),
                0, recipe.FrequencyBins - 1);
        }
    }
}
