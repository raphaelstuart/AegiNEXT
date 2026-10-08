using System.Numerics;

namespace AegiNext.Media.Analysis;

internal sealed class SpectrumTransform
{
    private readonly Complex[] values;
    private readonly SpectrumTransformPlan plan;

    internal SpectrumTransform(int size, AudioSpectrumWindow window = AudioSpectrumWindow.HANN)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if ((size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two.", nameof(size));
        }

        values = new Complex[size];
        plan = new(size, window);
    }

    internal void Power(ReadOnlySpan<float> samples, Span<double> destination)
    {
        if (samples.Length != values.Length || destination.Length < values.Length / 2 + 1)
        {
            throw new ArgumentException("FFT input or output length is invalid.");
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = new(samples[index] * plan.Window[index], 0);
        }

        for (var index = 1; index < values.Length; index++)
        {
            var reversed = plan.Reversed[index];
            if (index < reversed)
            {
                (values[index], values[reversed]) = (values[reversed], values[index]);
            }
        }

        var stage = 0;
        for (var size = 2; size <= values.Length; size <<= 1)
        {
            var factors = plan.Factors[stage++];
            for (var start = 0; start < values.Length; start += size)
            {
                for (var offset = 0; offset < size / 2; offset++)
                {
                    var even = values[start + offset];
                    var odd = factors[offset] * values[start + offset + size / 2];
                    values[start + offset] = even + odd;
                    values[start + offset + size / 2] = even - odd;
                }
            }
        }

        var scale = 1.0 / (values.Length * values.Length);
        for (var index = 0; index <= values.Length / 2; index++)
        {
            destination[index] = (values[index].Real * values[index].Real + values[index].Imaginary * values[index].Imaginary) * scale;
        }
    }
}
