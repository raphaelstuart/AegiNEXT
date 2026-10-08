using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisPipelineExecutionTests
{
    [Fact]
    public async Task CheckpointParksEveryWorkerAndAllowsAnotherProjectToFinish()
    {
        var directory = TemporaryDirectory();
        using var budget = new AudioAnalysisWorkerBudget(1);
        var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var duration = new MediaTime(12288, WaveformAnalyzer.SAMPLE_RATE);
            using var firstStore = new AudioAnalysisCacheStore(directory, "parked", new(MediaTime.Zero), duration);
            using var secondStore = new AudioAnalysisCacheStore(directory, "other-project", new(MediaTime.Zero), duration);
            var firstSource = new WindowAudioSource(_ => 0.25F, 12288);
            var secondSource = new WindowAudioSource(_ => -0.5F, 12288)
            {
                BeforeRead = _ => Assert.Equal(1, budget.ActiveWorkers)
            };
            var first = new AudioAnalysisCacheBuilder(_ => firstSource, new(MediaTime.Zero), duration,
                firstStore, 1, 3072, budget: budget);
            var second = new AudioAnalysisCacheBuilder(_ =>
            {
                Assert.Equal(1, budget.ActiveWorkers);
                return secondSource;
            }, new(MediaTime.Zero), duration, secondStore, 1, 3072, budget: budget);
            var firstBuild = first.BuildAsync(async token =>
            {
                Assert.Equal(0, budget.ActiveWorkers);
                parked.TrySetResult();
                await resume.Task.WaitAsync(token);
            });
            await parked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var reads = firstSource.ReadCount;
            await second.BuildAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(secondStore.IsComplete);
            Assert.False(firstStore.IsComplete);
            Assert.Equal(reads, firstSource.ReadCount);
            Assert.Equal(0, budget.ActiveWorkers);
            resume.SetResult();
            await firstBuild.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(firstStore.IsComplete);
            Assert.Equal(0, budget.ActiveWorkers);
        }
        finally
        {
            resume.TrySetResult();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ExecutionChangesApplyAfterDrainWithoutReseekingOrChangingAnyLod()
    {
        const int SAMPLE_COUNT = 300017;
        var directory = TemporaryDirectory();
        var duration = new MediaTime(SAMPLE_COUNT, WaveformAnalyzer.SAMPLE_RATE);
        var mapping = new MediaTimelineMapping(new(-511, WaveformAnalyzer.SAMPLE_RATE));
        var options = new AudioAnalysisOptions
        {
            Execution = new() { MaximumWorkers = 2, SegmentSamples = 49152 }
        };
        using var budget = new AudioAnalysisWorkerBudget(3);
        try
        {
            using var actual = new AudioAnalysisCacheStore(directory, "dynamic", mapping, duration);
            using var expected = new AudioAnalysisCacheStore(directory, "reference", mapping, duration);
            var source = new WindowAudioSource(Tone, SAMPLE_COUNT - 511, 733, -511);
            var builder = new AudioAnalysisCacheBuilder(_ => source, mapping, duration, actual,
                options: options, budget: budget);
            var initialWorkers = builder.EffectiveWorkers;
            var checkpoints = 0;
            await builder.BuildAsync(_ =>
            {
                Assert.Equal(0, budget.ActiveWorkers);
                if (++checkpoints == 1)
                {
                    builder.UpdateExecutionOptions(new() { MaximumWorkers = 1, MemoryBudgetMiB = 32, SegmentSamples = 98304 });
                    Assert.Equal(initialWorkers, builder.EffectiveWorkers);
                }
                else
                {
                    Assert.Equal(1, builder.EffectiveWorkers);
                    Assert.InRange(builder.PlannedWorkingBytes, 1, 24L * 1024 * 1024);
                }
                return Task.CompletedTask;
            });
            await new AudioAnalysisCacheBuilder(_ => new WindowAudioSource(Tone, SAMPLE_COUNT - 511, 317, -511),
                mapping, duration, expected, 1, 3072).BuildAsync();
            foreach (var resolution in new[] { 512, 2048, 16384, 1 << 30 })
            {
                var request = new WaveformAnalysisRequest(MediaTime.Zero, resolution, (SAMPLE_COUNT + resolution - 1) / resolution);
                Assert.Equal(expected.ReadWaveform(request, false)!.Peaks.ToArray(), actual.ReadWaveform(request, false)!.Peaks.ToArray());
                Assert.Equal(expected.ReadSpectrum(request, false)!.Levels.ToArray(), actual.ReadSpectrum(request, false)!.Levels.ToArray());
            }
            Assert.True(checkpoints > 1);
            Assert.Equal(1, source.SeekCount);
            Assert.Equal(SAMPLE_COUNT, source.FramesRead);
            Assert.Equal(0, budget.ActiveWorkers);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DecoderFailureDrainsPreviouslyStartedFftAndReleasesAllSharedPermits()
    {
        var directory = TemporaryDirectory();
        using var budget = new AudioAnalysisWorkerBudget(4);
        try
        {
            using var store = new AudioAnalysisCacheStore(directory, "invalid-pipeline", new(MediaTime.Zero), new(10));
            var source = new WindowAudioSource(sample => sample == 120000 ? float.NaN : Tone(sample), 480000);
            var builder = new AudioAnalysisCacheBuilder(_ => source, new(MediaTime.Zero), new(10), store,
                options: new() { Execution = new() { MaximumWorkers = 4, SegmentSamples = 98304 } }, budget: budget);
            await Assert.ThrowsAsync<InvalidDataException>(() => builder.BuildAsync());
            Assert.Equal(0, budget.ActiveWorkers);
            Assert.Equal(1, source.DisposeCount);
            Assert.False(store.IsComplete);
            using var nextProject = await budget.AcquireAsync(new());
            Assert.Equal(1, budget.ActiveWorkers);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MemoryBudgetLimitsDenseAnalysisWithoutRestoringTheOldFourWorkerCap()
    {
        var directory = TemporaryDirectory();
        try
        {
            var recipe = new AudioAnalysisRecipe
            {
                SpectrumSampleRate = 48000, FftSize = 512, HopDivisor = 8,
                FrequencyBins = 512, MaximumFrequency = 24000
            };
            using var dense = new AudioAnalysisCacheStore(directory, "dense", new(MediaTime.Zero), new(10), recipe: recipe);
            using var wide = new AudioAnalysisCacheStore(directory, "wide", new(MediaTime.Zero), new(10));
            using var budget = new AudioAnalysisWorkerBudget(16, 64);
            var bounded = new AudioAnalysisCacheBuilder(_ => throw new InvalidOperationException(),
                new(MediaTime.Zero), new(10), dense,
                options: new() { Recipe = recipe, Execution = new() { MaximumWorkers = 16, MemoryBudgetMiB = 32, SegmentSamples = 786432 } },
                budget: budget);
            Assert.Equal(1, bounded.EffectiveWorkers);
            Assert.InRange(bounded.PlannedWorkingBytes, 1, 24L * 1024 * 1024);
            var unrestricted = new AudioAnalysisCacheBuilder(_ => throw new InvalidOperationException(),
                new(MediaTime.Zero), new(10), wide,
                options: new() { Execution = new() { MaximumWorkers = 16, MemoryBudgetMiB = 512, SegmentSamples = 49152 } },
                budget: budget);
            Assert.Equal(Math.Min(16, AudioAnalysisExecutionOptions.HardwareMaximumWorkers), unrestricted.EffectiveWorkers);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static float Tone(long sample)
    {
        return (float)(0.3 * Math.Sin(sample * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-pipeline-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
