using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class VideoPreviewSeekSchedulingTests
{
    [Fact]
    public async Task RepeatedInFlightTargetSharesOneSeekAndDoesNotDiscardItsFrame()
    {
        var inner = new PreviewTestSource(10, 0, 40, 80, 120);
        var source = new BlockingPreviewSeekSource(inner);
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = Create(source, updates);
        await controller.OpenAsync("seek.mkv");
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == MediaTime.Zero);
        source.BlockNextSeek();
        try
        {
            var first = controller.SeekAsync(new(40, 1000));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var same = controller.SeekAsync(new(4, 100));
            Assert.Same(first, same);
            Assert.Same(first, controller.SeekAsync(new(1, 25)));
            source.Release();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(40, 1000));
            Assert.Single(source.SeekTargets);
            Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
            await controller.CloseAsync();
            Assert.Equal(1, inner.DisposeCount);
            Assert.All(inner.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
        }
        finally
        {
            source.Release();
        }
    }

    [Fact]
    public async Task DifferentTargetsRetainLatestGenerationAndDiscardIntermediateWork()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 40, 80, 120, 160));
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = Create(source, updates);
        await controller.OpenAsync("seek.mkv");
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == MediaTime.Zero);
        source.BlockNextSeek();
        try
        {
            var first = controller.SeekAsync(new(40, 1000));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var intermediate = controller.SeekAsync(new(80, 1000));
            var latest = controller.SeekAsync(new(120, 1000));
            source.Release();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => intermediate);
            await latest.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(120, 1000));
            Assert.Equal([new MediaTime(40, 1000), new(120, 1000)], source.SeekTargets.ToArray());
            Assert.DoesNotContain(updates, update => update.Frame?.Pixels.Span[0] is 11 or 12);
        }
        finally
        {
            source.Release();
        }
    }

    [Fact]
    public async Task CompletedSamePositionSeekStillRefreshesAnEditedScene()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 40, 80));
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = Create(source, updates);
        await controller.OpenAsync("seek.mkv");
        await controller.SeekAsync(new(40, 1000));
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(40, 1000));
        var count = updates.Count(update => update.Frame is not null);
        await controller.SeekAsync(new(40, 1000));
        await EventuallyAsync(() => updates.Count(update => update.Frame is not null) > count);
        Assert.Equal(2, source.SeekTargets.Count);
    }

    [Fact]
    public async Task PauseBreaksInFlightDeduplicationEvenWhenFollowingSeekUsesSameTarget()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 40, 80));
        await using var controller = Create(source, new());
        await controller.OpenAsync("seek.mkv");
        source.BlockNextSeek();
        try
        {
            var first = controller.SeekAsync(new(40, 1000));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var pause = controller.PauseAsync();
            var replacement = controller.SeekAsync(new(40, 1000));
            Assert.NotSame(first, replacement);
            source.Release();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pause);
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new MediaTime(40, 1000), controller.Snapshot.Position);
        }
        finally
        {
            source.Release();
        }
    }

    [Fact]
    public async Task QueuedStateCallbackFromAnObsoleteSeekDoesNotRefreshTheUi()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 40, 80, 120));
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        var converter = new PreviewTestConverter();
        var callbacks = new ConcurrentQueue<(Action Action, TaskCompletionSource Completion)>();
        var blockDispatch = false;
        Task Dispatch(Action action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Volatile.Read(ref blockDispatch))
            {
                action();
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            callbacks.Enqueue((action, completion));
            return completion.Task.WaitAsync(token);
        }

        await using var controller = new VideoPreviewController(Probe, (_, _) => new(_ => source),
            () => converter, Dispatch, updates.Enqueue);
        await controller.OpenAsync("seek.mkv");
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == MediaTime.Zero);
        converter.BlockNextConversion();
        Volatile.Write(ref blockDispatch, true);
        try
        {
            var first = controller.SeekAsync(new(40, 1000));
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => !callbacks.IsEmpty);
            var before = updates.Count;
            var latest = controller.SeekAsync(new(80, 1000));
            Assert.True(callbacks.TryDequeue(out var previous));
            previous.Action();
            Assert.Equal(before, updates.Count);
            Volatile.Write(ref blockDispatch, false);
            previous.Completion.TrySetResult();
            converter.Release();
            await Task.WhenAll(first, latest).WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(80, 1000));
        }
        finally
        {
            Volatile.Write(ref blockDispatch, false);
            converter.Release();
            while (callbacks.TryDequeue(out var callback))
            {
                callback.Completion.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task QualityInvalidationRejectsABlockedConversionAndRefreshesPausedPositionWithoutDeduplicatingOldWork()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 40, 80));
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        var converter = new PreviewTestConverter();
        await using var controller = new VideoPreviewController(Probe, (_, _) => new(_ => source), () => converter,
            (action, token) =>
            {
                token.ThrowIfCancellationRequested();
                action();
                return Task.CompletedTask;
            }, updates.Enqueue);
        await controller.OpenAsync("quality.mkv");
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == MediaTime.Zero);
        updates.Clear();
        converter.BlockNextConversion();
        try
        {
            var previous = controller.SeekAsync(new(40, 1000));
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.InvalidatePreview();
            var refresh = controller.RefreshPausedPreviewAsync();
            Assert.NotSame(previous, refresh);
            converter.Release();
            try
            {
                await previous.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(40, 1000));
            Assert.Single(updates, update => update.Frame is not null);
            Assert.Equal(2, source.SeekTargets.Count);
            Assert.Equal(new MediaTime(40, 1000), controller.Snapshot.Position);
            Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        }
        finally
        {
            converter.Release();
        }
    }

    private static VideoPreviewController Create(BlockingPreviewSeekSource source, ConcurrentQueue<VideoPreviewUpdate> updates)
    {
        return new(Probe, (_, _) => new(_ => source), () => new PreviewTestConverter(), (action, token) =>
        {
            token.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }, updates.Enqueue);
    }

    private static Task<VideoPreviewMedia> Probe(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1)));
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
