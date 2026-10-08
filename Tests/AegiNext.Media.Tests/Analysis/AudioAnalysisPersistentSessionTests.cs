using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisPersistentSessionTests
{
    [Fact]
    public async Task AllZoomLevelsReopenWithoutCreatingADecoder()
    {
        var directory = CreateDirectory();
        try
        {
            var source = new WindowAudioSource(index => (float)Math.Sin(index * 0.13), 48000);
            await using (var writer = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(1),
                             cacheDirectory: directory, cacheIdentity: "reopen", maximumWorkers: 1))
            {
                await writer.PrepareCacheAsync();
                Assert.True(writer.IsCacheComplete);
            }
            await using var reader = new AudioAnalysisSession(_ => throw new InvalidOperationException("No decoding on cache hit"),
                new(MediaTime.Zero), new(1), cacheDirectory: directory, cacheIdentity: "reopen");
            await reader.PrepareCacheAsync();
            foreach (var resolution in new[] { 512, 1024, 4096, 32768, 1 << 30 })
            {
                var result = await reader.GetWindowAsync(new(MediaTime.Zero, resolution, 4), true);
                Assert.Equal(4, result.Waveform.BucketCount);
                Assert.NotNull(result.Spectrogram);
                Assert.Contains(result.Waveform.Peaks.ToArray(), value => value != 0);
            }
            Assert.Equal(1, source.SeekCount);
            Assert.Equal(48000, source.FramesRead);
            Assert.Equal(1, source.DisposeCount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ACancelledViewportDoesNotCancelTheSingleProgressiveBuild()
    {
        var directory = CreateDirectory();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var source = new WindowAudioSource(_ => 0.4F, 8 * 48000);
            await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(8),
                cacheDirectory: directory, cacheIdentity: "progressive", maximumWorkers: 1);
            var build = session.PrepareCacheAsync(async token =>
            {
                published.TrySetResult();
                await resume.Task.WaitAsync(token);
            });
            Assert.Same(build, session.PrepareCacheAsync());
            await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var early = await session.GetAvailableLayersAsync(new(MediaTime.Zero, 1024, 4), true, true);
            Assert.NotNull(early.Waveform);
            Assert.NotNull(early.Spectrogram);
            var later = new WaveformAnalysisRequest(new(7), 1024, 4);
            var missing = await session.GetAvailableLayersAsync(later, true, true);
            Assert.Null(missing.Waveform);
            Assert.Null(missing.Spectrogram);
            using var cancellation = new CancellationTokenSource();
            var waiting = session.GetLayersAsync(later, true, true, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            Assert.False(build.IsCompleted);
            Assert.Equal(0, source.CancelCount);
            resume.TrySetResult();
            await build.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull((await session.GetLayersAsync(later, true, true)).Spectrogram);
            Assert.Equal(1, source.SeekCount);
        }
        finally
        {
            resume.TrySetResult();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CancelledBuildLeavesNoReusablePartialCacheAndCanRetry()
    {
        var directory = CreateDirectory();
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var opens = 0;
            await using var session = new AudioAnalysisSession(_ =>
            {
                Interlocked.Increment(ref opens);
                return new WindowAudioSource(_ => 0.2F, 6 * 48000);
            }, new(MediaTime.Zero), new(6), cacheDirectory: directory, cacheIdentity: "retry", maximumWorkers: 1);
            using var cancellation = new CancellationTokenSource();
            var first = session.PrepareCacheAsync(async token =>
            {
                checkpoint.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }, cancellationToken: cancellation.Token);
            await checkpoint.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.False(session.IsCacheComplete);
            Assert.Empty(Directory.EnumerateDirectories(directory));
            await session.PrepareCacheAsync();
            Assert.True(session.IsCacheComplete);
            Assert.Equal(2, opens);
            Assert.NotNull((await session.GetWindowAsync(new(MediaTime.Zero, 512, 2), true)).Spectrogram);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AdjacentFineRequestsReuseTheCurrentPcmReader()
    {
        var source = new WindowAudioSource(index => index % 2 == 0 ? -0.4F : 0.8F, 8192);
        await using var detail = new AudioWaveformDetailProvider(_ => source, new(MediaTime.Zero), new(1), 4096);
        var first = await detail.GetWaveformAsync(new(MediaTime.Zero, 1, 1024), CancellationToken.None);
        var second = await detail.GetWaveformAsync(new(new(1024, 48000), 1, 1024), CancellationToken.None);
        Assert.Equal(first.Peaks.ToArray(), second.Peaks.ToArray());
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task CorruptedCompletePayloadCanBeRebuiltWithTheSameIdentity()
    {
        var directory = CreateDirectory();
        try
        {
            string dataPath;
            await using (var first = new AudioAnalysisSession(_ => new WindowAudioSource(_ => 0.4F, 48000),
                             new(MediaTime.Zero), new(1), cacheDirectory: directory, cacheIdentity: "corruption"))
            {
                await first.PrepareCacheAsync();
                dataPath = Path.Combine(first.CacheDirectory, "data.bin");
            }
            await using (var file = File.OpenWrite(dataPath))
            {
                file.WriteByte(0xFF);
            }
            var opens = 0;
            await using var repaired = new AudioAnalysisSession(_ =>
            {
                Interlocked.Increment(ref opens);
                return new WindowAudioSource(_ => 0.4F, 48000);
            }, new(MediaTime.Zero), new(1), cacheDirectory: directory, cacheIdentity: "corruption");
            await Assert.ThrowsAsync<AudioAnalysisCacheCorruptionException>(() => repaired.GetWindowAsync(new(MediaTime.Zero, 512, 1), true));
            Assert.False(repaired.IsCacheComplete);
            await repaired.PrepareCacheAsync();
            Assert.Equal(0.4F, (await repaired.GetWindowAsync(new(MediaTime.Zero, 512, 1), true)).Waveform.Peaks.Span[1]);
            Assert.Equal(1, opens);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConcurrentSessionsWithTheSameIdentityShareTheCompletedWriterResult()
    {
        var directory = CreateDirectory();
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var first = new AudioAnalysisSession(_ => new WindowAudioSource(_ => 0.4F, 6 * 48000),
                new(MediaTime.Zero), new(6), cacheDirectory: directory, cacheIdentity: "shared", maximumWorkers: 1);
            await using var second = new AudioAnalysisSession(_ => throw new InvalidOperationException("Writer already exists"),
                new(MediaTime.Zero), new(6), cacheDirectory: directory, cacheIdentity: "shared", maximumWorkers: 1);
            var writer = first.PrepareCacheAsync(async token =>
            {
                checkpoint.TrySetResult();
                await resume.Task.WaitAsync(token);
            });
            await checkpoint.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reader = second.PrepareCacheAsync(_ =>
            {
                waiting.TrySetResult();
                return Task.CompletedTask;
            });
            await Task.WhenAny(waiting.Task, reader).WaitAsync(TimeSpan.FromSeconds(10));
            if (reader.IsCompleted)
            {
                await reader;
            }
            Assert.False(reader.IsCompleted);
            resume.TrySetResult();
            await Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(second.IsCacheComplete);
            Assert.Equal(0.4F, (await second.GetWindowAsync(new(MediaTime.Zero, 512, 1), true)).Waveform.Peaks.Span[1]);
        }
        finally
        {
            resume.TrySetResult();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task InvalidFineDecoderSamplesDoNotInvalidateAHealthyCompleteCache()
    {
        await using var session = new AudioAnalysisSession(_ => new WindowAudioSource(_ => 0.4F, 48000),
            new(MediaTime.Zero), new(1), detailSourceFactory: _ => new WindowAudioSource(_ => float.NaN, 48000));
        await session.PrepareCacheAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetWindowAsync(new(MediaTime.Zero, 1, 16), false));
        Assert.True(session.IsCacheComplete);
        Assert.Equal(0.4F, (await session.GetWindowAsync(new(MediaTime.Zero, 512, 1), true)).Waveform.Peaks.Span[1]);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
