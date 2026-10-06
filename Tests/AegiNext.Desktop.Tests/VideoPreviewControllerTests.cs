using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests;

public sealed class VideoPreviewControllerTests
{
    [Fact]
    public async Task ClosingMediaClearsTheCurrentRunAndAllowsAnotherFileToOpen()
    {
        var sources = new ConcurrentQueue<PreviewTestSource>([new(10, 0, 40), new(20, 0, 40)]);
        var issued = sources.ToArray();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController(ProbeAsync, (_, _) =>
        {
            Assert.True(sources.TryDequeue(out var source));
            return new(_ => source);
        }, () => new PreviewTestConverter(), DispatchImmediately, updates.Enqueue);
        await controller.OpenAsync("first.mkv");
        await EventuallyAsync(() => HasFrame(updates, 10));
        await controller.CloseMediaAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(controller.Snapshot.FilePath);
        Assert.Null(controller.MediaInfo);
        Assert.Equal(VideoPlaybackState.CREATED, controller.Snapshot.State);
        Assert.True(updates.Last().ClearFrame);
        Assert.Equal(1, issued[0].DisposeCount);
        await controller.OpenAsync("second.mkv");
        await EventuallyAsync(() => HasFrame(updates, 20));
        Assert.Equal("second.mkv", Path.GetFileName(controller.Snapshot.FilePath));
        await controller.CloseMediaAsync();
        await controller.CloseMediaAsync();
        Assert.Equal(1, issued[1].DisposeCount);
    }

    [Fact]
    public async Task ClosingMediaCancelsAnOpeningProbeAndDoesNotLeaveOpeningState()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new VideoPreviewController(async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(0, MediaTime.Zero, new(1));
        }, (_, _) => throw new InvalidOperationException("Cancelled probe must not create a video session."),
            () => new PreviewTestConverter(), DispatchImmediately, _ => { });
        var opening = controller.OpenAsync("blocked.mkv");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.CloseMediaAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.False(controller.Snapshot.IsOpening);
        Assert.Null(controller.Snapshot.FilePath);
        Assert.Equal(VideoPlaybackState.CREATED, controller.Snapshot.State);
    }

    [Fact]
    public async Task ReplacingAFileRejectsItsLateFrameEvenWhenBothSessionsHaveGenerationZero()
    {
        var sourceA = new PreviewTestSource(10, 0, 40);
        var sourceB = new PreviewTestSource(20, 0, 40);
        var converterA = new PreviewTestConverter();
        var converterB = new PreviewTestConverter();
        converterA.BlockNextConversion();
        var converters = new ConcurrentQueue<PreviewTestConverter>([converterA, converterB]);
        var sessions = new ConcurrentQueue<VideoPlaybackSession>();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController(ProbeAsync,
            (path, _) =>
            {
                var session = new VideoPlaybackSession(_ => Path.GetFileName(path) == "A.mkv" ? sourceA : sourceB);
                sessions.Enqueue(session);
                return session;
            }, () =>
            {
                Assert.True(converters.TryDequeue(out var converter));
                return converter;
            }, DispatchImmediately, updates.Enqueue);
        try
        {
            await controller.OpenAsync("A.mkv").WaitAsync(TimeSpan.FromSeconds(5));
            await converterA.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var originalEpoch = controller.Snapshot.Epoch;

            var replacement = controller.OpenAsync("B.mkv");
            converterA.Release();
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => HasFrame(updates, 20));

            Assert.True(controller.Snapshot.Epoch > originalEpoch);
            Assert.Equal("B.mkv", Path.GetFileName(controller.Snapshot.FilePath));
            Assert.All(sessions, session => Assert.Equal(0, session.Snapshot.Generation));
            Assert.False(HasFrame(updates, 10));
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            AssertReleased(sourceA, converterA);
            AssertReleased(sourceB, converterB);
        }
        finally
        {
            converterA.Release();
        }
    }

    [Fact]
    public async Task SeekingDuringConversionDiscardsThePreviousGenerationAndPublishesOnlyTheSelectedFrame()
    {
        var source = new PreviewTestSource(10, 0, 40, 100);
        var converter = new PreviewTestConverter();
        converter.BlockNextConversion();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates);
        try
        {
            await controller.OpenAsync("seek.mkv");
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await controller.SeekAsync(new(110, 1000)).WaitAsync(TimeSpan.FromSeconds(5));
            converter.Release();
            await EventuallyAsync(() => HasFrame(updates, 12));

            Assert.False(HasFrame(updates, 10));
            Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
            Assert.Equal(new MediaTime(100, 1000), controller.Snapshot.Position);
            Assert.Equal(1, converter.MaximumActiveCount);
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            AssertReleased(source, converter);
        }
        finally
        {
            converter.Release();
        }
    }

    [Fact]
    public async Task ClosingRejectsAQueuedUiCallbackEvenIfItExecutesAfterCancellationAndDisposal()
    {
        var source = new PreviewTestSource(10, 0, 40);
        var converter = new PreviewTestConverter();
        converter.BlockNextConversion();
        var dispatcher = new PreviewTestDispatcher();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, dispatcher: dispatcher);
        try
        {
            await controller.OpenAsync("pending.mkv");
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.BlockNextDispatch();
            converter.Release();
            await dispatcher.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, dispatcher.PendingCount);

            var firstClose = controller.CloseAsync();
            var repeatedClose = controller.CloseAsync();
            Assert.Same(firstClose, repeatedClose);
            await Task.WhenAll(firstClose, repeatedClose).WaitAsync(TimeSpan.FromSeconds(5));
            var updatesAtClose = updates.Count;
            dispatcher.RunPending();

            Assert.Equal(updatesAtClose, updates.Count);
            Assert.False(HasFrame(updates, 10));
            Assert.True(updates.Last().ClearFrame);
            Assert.Equal(VideoPlaybackState.CLOSED, controller.Snapshot.State);
            AssertReleased(source, converter);
        }
        finally
        {
            converter.Release();
            dispatcher.RunPending();
        }
    }

    [Fact]
    public async Task SeekingAfterEofRestartsTheConsumerWithoutRecreatingItsSourceOrConverter()
    {
        var source = new PreviewTestSource(10, 0, 40, 80);
        var converter = new PreviewTestConverter();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates);
        await controller.OpenAsync("eof.mkv");
        await EventuallyAsync(() => HasFrame(updates, 10));
        await controller.SeekAsync(new(1));
        await EventuallyAsync(() => HasFrame(updates, 12));
        await controller.PlayAsync();
        Assert.Equal(VideoPlaybackState.ENDED, controller.Snapshot.State);
        var frameCount = updates.Count(update => update.Frame is not null);

        await controller.SeekAsync(new(10, 1000)).WaitAsync(TimeSpan.FromSeconds(5));
        await EventuallyAsync(() => updates.Count(update => update.Frame is not null) > frameCount);

        Assert.Equal(10, updates.Last(update => update.Frame is not null).Frame!.Pixels.Span[0]);
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(new MediaTime(10, 1000), controller.Snapshot.Position);
        Assert.Equal(0, source.CancelCount);
        Assert.Equal(0, converter.DisposeCount);
        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        AssertReleased(source, converter);
    }

    [Fact]
    public async Task CancellingAProbeEndsOpeningAndAllowsAnotherFileToOpen()
    {
        var probeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PreviewTestSource(10, 0, 40);
        var converter = new PreviewTestConverter();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController(async (path, token) =>
        {
            if (Path.GetFileName(path) == "cancel.mkv")
            {
                probeEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return new(0, MediaTime.Zero, new MediaTime(1));
        }, (_, _) => new(_ => source), () => converter, DispatchImmediately, updates.Enqueue);
        using var cancellation = new CancellationTokenSource();
        var opening = controller.OpenAsync("cancel.mkv", cancellation.Token);
        await probeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(controller.Snapshot.IsOpening);
        Assert.NotEqual(VideoPlaybackState.OPENING, controller.Snapshot.State);
        await controller.OpenAsync("valid.mkv").WaitAsync(TimeSpan.FromSeconds(5));
        await EventuallyAsync(() => HasFrame(updates, 10));
        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        AssertReleased(source, converter);
    }

    [Fact]
    public async Task ClosingAnOpenWithABlockedSourceFactoryCancelsItAndDrainsTheOpeningOperation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returnedSource = new PreviewTestSource(10, 0);
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController(ProbeAsync,
            (_, _) => new(token =>
            {
                entered.TrySetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return returnedSource;
            }), () => throw new InvalidOperationException("The cancelled source must never start conversion."),
            DispatchImmediately, updates.Enqueue);
        var opening = controller.OpenAsync("blocked.mkv");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.False(controller.Snapshot.IsOpening);
        Assert.Equal(VideoPlaybackState.CLOSED, controller.Snapshot.State);
        Assert.All(updates, update => Assert.Null(update.Frame));
        Assert.Equal(0, returnedSource.DisposeCount);
    }

    [Fact]
    public async Task CancellingOpenAfterTheSourceFactoryStartsActivelyClosesItsSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PreviewTestSource(10, 0);
        var sessions = new ConcurrentQueue<VideoPlaybackSession>();
        await using var controller = new VideoPreviewController(ProbeAsync, (_, _) =>
        {
            var session = new VideoPlaybackSession(token =>
            {
                entered.TrySetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
                return source;
            });
            sessions.Enqueue(session);
            return session;
        }, () => throw new InvalidOperationException("A cancelled open must not create a converter."),
            DispatchImmediately, _ => { });
        using var cancellation = new CancellationTokenSource();
        var opening = controller.OpenAsync("cancel-factory.mkv", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.False(controller.Snapshot.IsOpening);
        Assert.Equal(VideoPlaybackState.CLOSED, Assert.Single(sessions).Snapshot.State);
        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SustainedSlowConversionKeepsPresentingCompletedFramesWithoutSlowingThePlaybackClock()
    {
        var source = new PreviewTestSource(10, 0, 33, 66, 99, 132, 165, 198, 231, 264, 297);
        var clock = new ManualPlaybackTimeProvider();
        var converter = new PreviewTestConverter
        {
            ConversionWork = marker =>
            {
                if (marker != 19)
                {
                    clock.WaitForScheduledTimerAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                }

                clock.Advance(TimeSpan.FromMilliseconds(40));
            }
        };
        converter.BlockNextConversion();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, clock);
        try
        {
            await controller.OpenAsync("slow.mkv");
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var epoch = controller.Snapshot.Epoch;
            await controller.PlayAsync();
            await EventuallyAsync(() => clock.ActiveTimerCount != 0);

            converter.Release();
            await EventuallyAsync(() => HasFrame(updates, 19));
            await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(297, 1000));

            var presented = updates.Where(update => update.Frame is not null).ToArray();
            Assert.True(presented.Length >= 6,
                $"Every conversion exceeds a frame interval, but playback must keep presenting; received {presented.Length} frames.");
            Assert.Equal(10, presented[0].Frame!.Pixels.Span[0]);
            Assert.All(presented, update =>
            {
                Assert.Equal(epoch, update.Snapshot.Epoch);
                var frameTime = new MediaTime((update.Frame!.Pixels.Span[0] - 10) * 33, 1000);
                Assert.Equal(frameTime, update.Snapshot.PresentedFrameTime);
                Assert.Equal(update.Snapshot.Position, update.Snapshot.PresentedAtPosition);
                Assert.Equal(0, update.Snapshot.PresentedGeneration);
            });
            for (var index = 1; index < presented.Length; index++)
            {
                Assert.True(presented[index].Frame!.Pixels.Span[0] > presented[index - 1].Frame!.Pixels.Span[0]);
            }

            var playing = presented.Where(update => update.Snapshot.State == VideoPlaybackState.PLAYING).ToArray();
            Assert.True(playing.Length >= 5);
            for (var index = 0; index < playing.Length; index++)
            {
                Assert.Equal(new MediaTime((index + 1) * 40, 1000), playing[index].Snapshot.Position);
                var frameTime = new MediaTime((playing[index].Frame!.Pixels.Span[0] - 10) * 33, 1000);
                Assert.True(playing[index].Snapshot.Position > frameTime);
            }

            Assert.Equal(converter.ConversionCount * TimeSpan.FromMilliseconds(40).Ticks, clock.GetTimestamp());
            Assert.Equal(VideoPlaybackState.ENDED, controller.Snapshot.State);
            Assert.Equal(new MediaTime(297, 1000), controller.Snapshot.Position);
            Assert.Equal(new MediaTime(297, 1000), controller.Snapshot.PresentedFrameTime);
            Assert.Equal(new MediaTime(297, 1000), controller.Snapshot.PresentedAtPosition);
            Assert.Equal(0, controller.Snapshot.PresentedGeneration);
            Assert.Equal(1, converter.MaximumActiveCount);
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(controller.Snapshot.PresentedFrameTime);
            Assert.Null(controller.Snapshot.PresentedAtPosition);
            Assert.Null(controller.Snapshot.PresentedGeneration);
            AssertReleased(source, converter);
        }
        finally
        {
            converter.Release();
        }
    }

    private static VideoPreviewController CreateController(PreviewTestSource source, PreviewTestConverter converter,
        ConcurrentQueue<VideoPreviewUpdate> updates, TimeProvider? clock = null, PreviewTestDispatcher? dispatcher = null)
    {
        return new(ProbeAsync, (_, _) => new(_ => source, clock), () => converter,
            dispatcher is null ? DispatchImmediately : dispatcher.DispatchAsync, updates.Enqueue);
    }

    private static Task<VideoPreviewMedia> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1)));
    }

    private static Task DispatchImmediately(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    private static bool HasFrame(ConcurrentQueue<VideoPreviewUpdate> updates, byte marker)
    {
        return updates.Any(update => update.Frame?.Pixels.Span[0] == marker);
    }

    [SuppressMessage("ReSharper", "ParameterOnlyUsedForPreconditionCheck.Local", Justification = "Assertions are the purpose of this resource ownership verification helper.")]
    private static void AssertReleased(PreviewTestSource source, PreviewTestConverter converter)
    {
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, converter.DisposeCount);
        Assert.All(source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
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
