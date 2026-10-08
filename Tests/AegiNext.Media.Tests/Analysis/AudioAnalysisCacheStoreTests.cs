using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisCacheStoreTests
{
    [Fact]
    public void CompletedBinaryCacheReopensWithoutASampleSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new AudioAnalysisCacheStore(directory, "fixture", new(MediaTime.Zero), new(1024, 48000)))
            {
                writer.AppendWaveform(512, 0, [-0.4F, 0.8F, -0.2F, 0.6F]);
                writer.AppendSpectrum(256, 0, 2, Enumerable.Range(0, 256).Select(index => (byte)index).ToArray());
                writer.Publish(1024);
                writer.Complete(1024);
            }
            using var reader = new AudioAnalysisCacheStore(directory, "fixture", new(MediaTime.Zero), new(1024, 48000));
            Assert.True(reader.IsComplete);
            var request = new WaveformAnalysisRequest(MediaTime.Zero, 512, 2);
            Assert.Equal(new[] { -0.4F, 0.8F, -0.2F, 0.6F }, reader.ReadWaveform(request, false)!.Peaks.ToArray());
            Assert.Equal(Enumerable.Range(0, 256).Select(index => (byte)index).ToArray(), reader.ReadSpectrum(request, false)!.Levels.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void UnfinishedBucketsAreUnavailableRatherThanPublishedAsSilence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new AudioAnalysisCacheStore(directory, "partial", new(MediaTime.Zero), new(2));
            store.AppendWaveform(512, 0, [-0.5F, 0.5F]);
            store.Publish(512);
            var request = new WaveformAnalysisRequest(MediaTime.Zero, 512, 2);
            Assert.Null(store.ReadWaveform(request, false));
            Assert.Equal(1, store.ReadWaveform(request, true)!.BucketCount);
            Assert.Null(store.ReadWaveform(new(new(1), 512, 1), true));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CorruptedPayloadIsRejectedAfterReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        try
        {
            string cachePath;
            using (var writer = new AudioAnalysisCacheStore(directory, "corrupt", new(MediaTime.Zero), new(512, 48000)))
            {
                writer.AppendWaveform(512, 0, [-0.4F, 0.8F]);
                writer.Complete(512);
                cachePath = Path.Combine(writer.DirectoryPath, "data.bin");
            }
            using (var file = File.Open(cachePath, FileMode.Open, FileAccess.Write))
            {
                file.WriteByte(0xFF);
            }
            using var reader = new AudioAnalysisCacheStore(directory, "corrupt", new(MediaTime.Zero), new(512, 48000));
            Assert.Throws<InvalidDataException>(() => reader.ReadWaveform(new(MediaTime.Zero, 512, 1), false));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MigrationToAnExistingCacheKeepsTheCopiedIndexAndPayloadLayoutTogether()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(directory, "first");
        var secondRoot = Path.Combine(directory, "second");
        try
        {
            using var first = new AudioAnalysisCacheStore(firstRoot, "layout", new(MediaTime.Zero), new(1024, 48000), 0);
            first.AppendWaveform(512, 0, [-0.4F, 0.8F]);
            first.AppendWaveform(512, 1, [-0.2F, 0.6F]);
            first.Complete(1024);
            using (var existing = new AudioAnalysisCacheStore(secondRoot, "layout", new(MediaTime.Zero), new(1024, 48000)))
            {
                existing.AppendWaveform(512, 0, [-0.4F, 0.8F, -0.2F, 0.6F]);
                existing.Complete(1024);
            }
            await first.RelocateAsync(secondRoot, null, CancellationToken.None);
            Assert.Equal(new[] { -0.4F, 0.8F, -0.2F, 0.6F }, first.ReadWaveform(new(MediaTime.Zero, 512, 2), false)!.Peaks.ToArray());
            using var reopened = new AudioAnalysisCacheStore(secondRoot, "layout", new(MediaTime.Zero), new(1024, 48000));
            Assert.True(reopened.IsComplete);
            Assert.Equal(first.ReadWaveform(new(MediaTime.Zero, 512, 2), false)!.Peaks.ToArray(),
                reopened.ReadWaveform(new(MediaTime.Zero, 512, 2), false)!.Peaks.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CancelledMigrationLeavesTheOriginalCacheReadableAndRemovesPartialCopies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(directory, "second");
        try
        {
            using var store = new AudioAnalysisCacheStore(Path.Combine(directory, "first"), "cancel-copy",
                new(MediaTime.Zero), new(512, 48000));
            store.AppendWaveform(512, 0, [-0.4F, 0.8F]);
            store.Complete(512);
            var original = store.DirectoryPath;
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RelocateAsync(destination, _ =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cancellation.Token));
            Assert.Equal(original, store.DirectoryPath);
            Assert.Equal(0.8F, store.ReadWaveform(new(MediaTime.Zero, 512, 1), false)!.Peaks.Span[1]);
            Assert.Empty(Directory.EnumerateDirectories(destination));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SourceRebuildDuringMigrationCannotMixOldPayloadAndNewIndex()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(directory, "source");
        var destinationRoot = Path.Combine(directory, "destination");
        try
        {
            using var original = new AudioAnalysisCacheStore(sourceRoot, "snapshot", new(MediaTime.Zero), new(1024, 48000), 0);
            original.AppendWaveform(512, 0, [-0.4F, 0.8F]);
            original.AppendWaveform(512, 1, [-0.2F, 0.6F]);
            original.Complete(1024);
            var replaced = false;
            await original.RelocateAsync(destinationRoot, _ =>
            {
                if (!replaced)
                {
                    using var rebuild = new AudioAnalysisCacheStore(sourceRoot, "snapshot", new(MediaTime.Zero), new(1024, 48000));
                    rebuild.Invalidate();
                    rebuild.AppendWaveform(512, 0, [-0.1F, 0.2F, -0.3F, 0.4F]);
                    rebuild.Complete(1024);
                    replaced = true;
                }
                return Task.CompletedTask;
            }, CancellationToken.None);
            using var reopened = new AudioAnalysisCacheStore(destinationRoot, "snapshot", new(MediaTime.Zero), new(1024, 48000));
            Assert.True(replaced);
            Assert.True(reopened.IsComplete);
            var request = new WaveformAnalysisRequest(MediaTime.Zero, 512, 2);
            Assert.Equal(new[] { -0.4F, 0.8F, -0.2F, 0.6F }, reopened.ReadWaveform(request, false)!.Peaks.ToArray());
            Assert.Equal(original.ReadWaveform(request, false)!.Peaks.ToArray(), reopened.ReadWaveform(request, false)!.Peaks.ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OpeningDuringReplacementWaitsForTheCommittedIndexAndDataPair()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        var mapping = new MediaTimelineMapping(MediaTime.Zero);
        var duration = new MediaTime(512, 48000);
        try
        {
            using var rebuild = new AudioAnalysisCacheStore(directory, "open-pair", mapping, duration);
            rebuild.AppendWaveform(512, 0, [-0.1F, 0.2F]);
            rebuild.Complete(512);
            rebuild.Invalidate();
            rebuild.AppendWaveform(512, 0, [-0.4F, 0.8F]);
            using var reader = new AudioAnalysisCacheStore(directory, "open-pair", mapping, duration);
            Assert.False(reader.IsComplete);
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var builder = new AudioAnalysisCacheBuilder(_ => throw new InvalidOperationException("Committed cache should be reused"),
                mapping, duration, reader);
            var pending = builder.BuildAsync(_ =>
            {
                waiting.TrySetResult();
                return Task.CompletedTask;
            });
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            rebuild.Complete(512);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(reader.IsComplete);
            Assert.Equal(0.8F, reader.ReadWaveform(new(MediaTime.Zero, 512, 1), false)!.Peaks.Span[1]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
