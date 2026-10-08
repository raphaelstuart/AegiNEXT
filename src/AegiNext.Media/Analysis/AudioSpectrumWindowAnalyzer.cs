using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

internal static class AudioSpectrumWindowAnalyzer
{
    internal const int FIR_HALF = 31;
    internal const int RAW_PADDING = SpectrogramAnalyzer.FFT_SIZE / 2 * 3 + FIR_HALF;
    private static readonly double[] filter = CreateFilter();
    private static readonly int[] rows = CreateRows();
    private static readonly AudioAnalysisRecipe defaultRecipe = new();

    internal static byte[] AnalyzeSegment(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter,
        int columns, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new AudioSpectrumSegmentProcessor().Analyze(rawSamples, rawFirstSample, firstCenter, columns, cancellationToken);
    }

    internal static byte[] AnalyzeSegment(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter,
        int columns, Span<float> samples, Span<double> power, SpectrumTransform transform, CancellationToken cancellationToken)
    {
        var plan = new AudioSpectrumAnalysisPlan(new());
        return AnalyzeSegment(rawSamples, rawFirstSample, firstCenter, columns, samples, power, transform,
            plan, new double[plan.Recipe.FrequencyBins], cancellationToken);
    }

    internal static byte[] AnalyzeSegment(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter,
        int columns, Span<float> samples, Span<double> power, SpectrumTransform transform,
        AudioSpectrumAnalysisPlan plan, Span<double> rowPower, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        var levels = new byte[checked(columns * plan.Recipe.FrequencyBins)];
        AnalyzeSegmentInto(rawSamples, rawFirstSample, firstCenter, columns, samples, power, transform,
            plan, rowPower, levels, cancellationToken);
        return levels;
    }

    internal static void AnalyzeSegmentInto(ReadOnlySpan<float> rawSamples, long rawFirstSample, long firstCenter,
        int columns, Span<float> samples, Span<double> power, SpectrumTransform transform,
        AudioSpectrumAnalysisPlan plan, Span<double> rowPower, Span<byte> levels, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        var recipe = plan.Recipe;
        if (levels.Length != checked(columns * recipe.FrequencyBins))
        {
            throw new ArgumentException("频谱输出缓冲长度不匹配。", nameof(levels));
        }
        var firstSample = checked(firstCenter - recipe.FftSize / 2);
        var count = checked((columns - 1) * recipe.HopSize + recipe.FftSize);
        var firstRaw = checked(firstSample * recipe.Decimation - FIR_HALF - rawFirstSample);
        var afterRaw = checked(firstRaw + (long)(count - 1) * recipe.Decimation + plan.Filter.Length);
        if (firstRaw < 0 || afterRaw > rawSamples.Length || samples.Length != count)
        {
            throw new ArgumentException("分段 PCM 未包含完整的频谱窗口与低通滤波边界。", nameof(rawSamples));
        }

        for (var index = 0; index < count; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            double value = 0;
            var offset = checked((int)firstRaw + index * recipe.Decimation);
            for (var tap = 0; tap < plan.Filter.Length; tap++)
            {
                value += rawSamples[offset + tap] * plan.Filter[tap];
            }
            samples[index] = (float)value;
        }

        for (var column = 0; column < columns; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            transform.Power(samples.Slice(column * recipe.HopSize, recipe.FftSize), power);
            WriteColumn(power, levels, columns, column, recipe, plan.Rows, rowPower);
        }
    }

    internal static SpectrogramData Analyze(Func<long, float> readSample, Action<long> prepareCenter,
        MediaTime origin, long firstCenter, int samplesPerColumn, int columns, long mediaStart, Func<long> mediaEnd,
        Action checkRequest)
    {
        var halfColumn = samplesPerColumn / 2;
        var firstWindow = firstCenter;
        var lastWindow = firstCenter + (long)columns * samplesPerColumn;
        checkRequest();
        var raw = new float[SpectrogramAnalyzer.FFT_SIZE * 3 + FIR_HALF * 2];
        var samples = new float[SpectrogramAnalyzer.FFT_SIZE];
        var power = new double[SpectrogramAnalyzer.FFT_SIZE / 2 + 1];
        var transform = new SpectrumTransform(SpectrogramAnalyzer.FFT_SIZE);
        var levels = new byte[checked(columns * SpectrogramAnalyzer.FREQUENCY_BINS)];
        for (var center = firstWindow; center < lastWindow; center += samplesPerColumn)
        {
            checkRequest();
            if (center * 3 < mediaStart || center * 3 >= mediaEnd())
            {
                continue;
            }
            prepareCenter(center * 3);
            if (center * 3 >= mediaEnd())
            {
                continue;
            }
            var rawStart = checked((center - SpectrogramAnalyzer.FFT_SIZE / 2) * 3 - FIR_HALF);
            for (var index = 0; index < raw.Length; index++)
            {
                raw[index] = readSample(rawStart + index);
            }
            for (var index = 0; index < samples.Length; index++)
            {
                double value = 0;
                for (var tap = 0; tap < filter.Length; tap++)
                {
                    value += raw[index * 3 + tap] * filter[tap];
                }
                samples[index] = (float)value;
            }
            transform.Power(samples, power);
            var column = (int)((center - firstCenter) / samplesPerColumn);
            WriteColumn(power, levels, columns, column);
        }
        checkRequest();
        return new(columns, SpectrogramAnalyzer.FREQUENCY_BINS,
            new MediaTime(firstCenter - halfColumn, SpectrogramAnalyzer.SAMPLE_RATE) - origin,
            new MediaTime(samplesPerColumn, SpectrogramAnalyzer.SAMPLE_RATE), levels);
    }

    internal static long Floor(long value, long step)
    {
        var remainder = value % step;
        return value - remainder - (remainder < 0 ? step : 0);
    }

    private static void WriteColumn(ReadOnlySpan<double> power, Span<byte> levels, int columns, int column)
    {
        Span<double> rowPower = stackalloc double[SpectrogramAnalyzer.FREQUENCY_BINS];
        WriteColumn(power, levels, columns, column, defaultRecipe, rows, rowPower);
    }

    private static void WriteColumn(ReadOnlySpan<double> power, Span<byte> levels, int columns, int column,
        AudioAnalysisRecipe recipe, ReadOnlySpan<int> frequencyRows, Span<double> rowPower)
    {
        rowPower.Clear();
        for (var bin = 1; bin < power.Length; bin++)
        {
            var row = frequencyRows[bin];
            if (row >= 0)
            {
                rowPower[row] = Math.Max(rowPower[row], power[bin]);
            }
        }
        for (var row = 0; row < rowPower.Length; row++)
        {
            var decibels = 10 * Math.Log10(Math.Max(rowPower[row], 1e-20));
            var intensity = (byte)Math.Clamp((decibels - recipe.MinimumDecibels) * 255 /
                                            (recipe.MaximumDecibels - recipe.MinimumDecibels), 0, 255);
            levels[row * columns + column] = intensity;
        }
    }

    private static double[] CreateFilter()
    {
        const double CUTOFF = 7000.0 / WaveformAnalyzer.SAMPLE_RATE;
        var result = new double[FIR_HALF * 2 + 1];
        double sum = 0;
        for (var index = 0; index < result.Length; index++)
        {
            var position = index - FIR_HALF;
            var sinc = position == 0 ? 2 * CUTOFF : Math.Sin(2 * Math.PI * CUTOFF * position) / (Math.PI * position);
            result[index] = sinc * (0.54 - 0.46 * Math.Cos(2 * Math.PI * index / (result.Length - 1)));
            sum += result[index];
        }
        for (var index = 0; index < result.Length; index++)
        {
            result[index] /= sum;
        }
        return result;
    }

    private static int[] CreateRows()
    {
        var result = new int[SpectrogramAnalyzer.FFT_SIZE / 2 + 1];
        var span = Math.Log(SpectrogramAnalyzer.SAMPLE_RATE / 2.0 / 40);
        for (var bin = 1; bin < result.Length; bin++)
        {
            var frequency = bin * SpectrogramAnalyzer.SAMPLE_RATE / (double)SpectrogramAnalyzer.FFT_SIZE;
            result[bin] = Math.Clamp((int)(Math.Log(Math.Max(40, frequency) / 40) / span * SpectrogramAnalyzer.FREQUENCY_BINS),
                0, SpectrogramAnalyzer.FREQUENCY_BINS - 1);
        }
        return result;
    }
}
