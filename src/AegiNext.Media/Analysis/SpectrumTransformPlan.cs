using System.Numerics;

namespace AegiNext.Media.Analysis;

internal sealed class SpectrumTransformPlan
{
    internal double[] Window { get; }
    internal int[] Reversed { get; }
    internal Complex[][] Factors { get; }

    internal SpectrumTransformPlan(int size, AudioSpectrumWindow window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if ((size & (size - 1)) != 0 || !Enum.IsDefined(window))
        {
            throw new ArgumentException("FFT 网格或窗函数无效。");
        }
        Window = new double[size];
        Reversed = new int[size];
        Factors = new Complex[BitOperations.Log2((uint)size)][];
        for (var index = 0; index < size; index++)
        {
            var phase = 2 * Math.PI * index / (size - 1);
            Window[index] = window switch
            {
                AudioSpectrumWindow.HANN => 0.5 - 0.5 * Math.Cos(phase),
                AudioSpectrumWindow.HAMMING => 0.54 - 0.46 * Math.Cos(phase),
                AudioSpectrumWindow.BLACKMAN => 0.42 - 0.5 * Math.Cos(phase) + 0.08 * Math.Cos(phase * 2),
                _ => throw new ArgumentOutOfRangeException(nameof(window))
            };
        }
        var reversed = 0;
        for (var index = 1; index < size; index++)
        {
            var bit = size >> 1;
            while ((reversed & bit) != 0)
            {
                reversed ^= bit;
                bit >>= 1;
            }
            reversed ^= bit;
            Reversed[index] = reversed;
        }
        var stage = 0;
        for (var length = 2; length <= size; length <<= 1)
        {
            var factors = new Complex[length / 2];
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            var factor = Complex.One;
            for (var index = 0; index < factors.Length; index++)
            {
                factors[index] = factor;
                factor *= step;
            }
            Factors[stage++] = factors;
        }
    }
}
