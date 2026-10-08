using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisOptionsTests
{
    [Fact]
    public void DefaultsPreserveTheExistingGridAndCacheSize()
    {
        var options = new AudioAnalysisOptions();
        options.Validate();
        Assert.Equal(256, options.Recipe.HopSize);
        Assert.Equal(3, options.Recipe.Decimation);
        Assert.Equal(1567, options.Recipe.RawPadding);
        Assert.Equal(64, options.Recipe.WindowMilliseconds);
        Assert.Equal(16, options.Recipe.HopMilliseconds);
        Assert.Equal(63000000, options.Recipe.EstimatedCacheBytesPerSecond * 3600);
        Assert.Equal(16L * 1024 * 1024, options.Execution.ReadCacheBytes);
        Assert.Equal(48L * 1024 * 1024, options.Execution.WorkingBytes);
    }

    [Fact]
    public void PortableThreadRequestIsClampedWithoutRejectingPreferences()
    {
        var execution = new AudioAnalysisExecutionOptions { MaximumWorkers = 1024 };
        execution.Validate();
        Assert.InRange(execution.EffectiveMaximumWorkers, 1, AudioAnalysisExecutionOptions.HardwareMaximumWorkers);
    }

    [Theory]
    [InlineData(8000, 4096, 8, 512)]
    [InlineData(24000, 512, 2, 64)]
    [InlineData(48000, 2048, 4, 256)]
    public void SupportedRecipesHaveExactIntegralGrids(int sampleRate, int fftSize, int hopDivisor, int rows)
    {
        var recipe = new AudioAnalysisRecipe
        {
            SpectrumSampleRate = sampleRate, FftSize = fftSize, HopDivisor = hopDivisor, FrequencyBins = rows,
            MaximumFrequency = sampleRate / 2.0
        };
        recipe.Validate();
        Assert.Equal(48000, recipe.SpectrumSampleRate * recipe.Decimation);
        Assert.Equal(0, new AudioAnalysisExecutionOptions().SegmentSamples % (recipe.HopSize * recipe.Decimation));
    }

    [Fact]
    public void NyquistAndDbRangeValidationRejectsMisleadingSettings()
    {
        Assert.Throws<InvalidDataException>(() => new AudioAnalysisRecipe { SpectrumSampleRate = 8000 }.Validate());
        Assert.Throws<InvalidDataException>(() => new AudioAnalysisRecipe { MinimumFrequency = 8000 }.Validate());
        Assert.Throws<InvalidDataException>(() => new AudioAnalysisRecipe { MinimumDecibels = -9 }.Validate());
        Assert.Throws<InvalidDataException>(() => new AudioAnalysisDisplayOptions { SpectrumBrightness = double.NaN }.Validate());
    }
}
