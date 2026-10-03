using System.Numerics;

namespace AegiNext.Media.Analysis;

internal sealed class SpectrumTransform
{
    private readonly Complex[] values;
    private readonly double[] window;

    internal SpectrumTransform(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if ((size & (size - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a power of two.", nameof(size));
        }

        values = new Complex[size];
        window = new double[size];
        for (var index = 0; index < size; index++)
        {
            window[index] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (size - 1));
        }
    }

    internal void Power(ReadOnlySpan<float> samples, Span<double> destination)
    {
        if (samples.Length != values.Length || destination.Length < values.Length / 2 + 1)
        {
            throw new ArgumentException("FFT input or output length is invalid.");
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = new(samples[index] * window[index], 0);
        }

        var reversed = 0;
        for (var index = 1; index < values.Length; index++)
        {
            var bit = values.Length >> 1;
            while ((reversed & bit) != 0)
            {
                reversed ^= bit;
                bit >>= 1;
            }

            reversed ^= bit;
            if (index < reversed)
            {
                (values[index], values[reversed]) = (values[reversed], values[index]);
            }
        }

        for (var size = 2; size <= values.Length; size <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (var start = 0; start < values.Length; start += size)
            {
                var factor = Complex.One;
                for (var offset = 0; offset < size / 2; offset++)
                {
                    var even = values[start + offset];
                    var odd = factor * values[start + offset + size / 2];
                    values[start + offset] = even + odd;
                    values[start + offset + size / 2] = even - odd;
                    factor *= step;
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
