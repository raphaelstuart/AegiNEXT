using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal static class AudioSpectrumWindowAnalyzer
{
    internal const int FIR_HALF = 31;
    private static readonly double[] filter = CreateFilter();
    private static readonly int[] rows = CreateRows();

    internal static SpectrogramData Analyze(IAudioSampleSource source, MediaTime origin, long firstCenter,
        int samplesPerColumn, int columns, Action checkRequest, CancellationToken lifetime)
    {
        var halfColumn = samplesPerColumn / 2;
        var firstWindow = firstCenter;
        var lastWindow = firstCenter + (long)columns * samplesPerColumn;
        var rawStart = checked((firstWindow - SpectrogramAnalyzer.FFT_SIZE / 2) * 3 - FIR_HALF);
        source.Seek(new(rawStart, WaveformAnalyzer.SAMPLE_RATE), lifetime);
        checkRequest();
        var reader = new AudioAnalysisPcmReader(source, checkRequest, lifetime);
        var raw = new float[SpectrogramAnalyzer.FFT_SIZE * 3 + FIR_HALF * 2];
        var samples = new float[SpectrogramAnalyzer.FFT_SIZE];
        var power = new double[SpectrogramAnalyzer.FFT_SIZE / 2 + 1];
        var transform = new SpectrumTransform(SpectrogramAnalyzer.FFT_SIZE);
        var levels = new byte[checked(columns * SpectrogramAnalyzer.FREQUENCY_BINS)];
        for (var index = 0; index < raw.Length; index++)
        {
            raw[index] = reader.Read(rawStart + index);
        }
        for (var center = firstWindow; center < lastWindow; center += samplesPerColumn)
        {
            checkRequest();
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
            for (var bin = 1; bin < power.Length; bin++)
            {
                var decibels = 10 * Math.Log10(Math.Max(power[bin], 1e-12));
                var intensity = (byte)Math.Clamp((decibels + 80) * 255 / 80, 0, 255);
                var offset = rows[bin] * columns + column;
                levels[offset] = Math.Max(levels[offset], intensity);
            }
            if (center + samplesPerColumn >= lastWindow)
            {
                break;
            }
            var step = samplesPerColumn * 3;
            rawStart += step;
            if (step >= raw.Length)
            {
                source.Seek(new(rawStart, WaveformAnalyzer.SAMPLE_RATE), lifetime);
                checkRequest();
                reader = new(source, checkRequest, lifetime);
                for (var index = 0; index < raw.Length; index++)
                {
                    raw[index] = reader.Read(rawStart + index);
                }
            }
            else
            {
                Array.Copy(raw, step, raw, 0, raw.Length - step);
                for (var index = raw.Length - step; index < raw.Length; index++)
                {
                    raw[index] = reader.Read(rawStart + index);
                }
            }
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
