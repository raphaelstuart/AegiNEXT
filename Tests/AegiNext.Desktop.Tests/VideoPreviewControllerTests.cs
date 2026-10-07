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
    public async Task AFailedProbeRetainsItsErrorAfterRetirementAndCanBeClearedAndReopened()
    {
        var failure = new IOException("The probe failed.");
        var source = new PreviewTestSource(10, 0, 40);
        var converter = new PreviewTestConverter();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController((path, _) =>
        {
            return Path.GetFileName(path) == "failed.mkv"
                ? Task.FromException<VideoPreviewMedia>(failure)
                : Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1)));
        }, (_, _) => new(_ => source), () => converter, DispatchImmediately, updates.Enqueue);

        var actual = await Assert.ThrowsAsync<IOException>(() => controller.OpenAsync("failed.mkv"));

        Assert.Same(failure, actual);
        Assert.Same(failure, controller.Snapshot.Error);
        Assert.Equal(VideoPlaybackState.FAULTED, controller.Snapshot.State);
        Assert.False(controller.Snapshot.IsOpening);
        await controller.CloseMediaAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(controller.Snapshot.Error);
        Assert.Equal(VideoPlaybackState.CREATED, controller.Snapshot.State);
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
    public async Task SustainedSlowConversionSkipsFramesAndPresentsValidIntervalsWithoutSlowingThePlaybackClock()
    {
        var source = new PreviewTestSource(10, 0, 33, 66, 99, 132, 165, 198, 231, 264, 297);
        var clock = new ManualPlaybackTimeProvider();
        var converter = new PreviewTestConverter
        {
            ConversionWork = marker =>
            {
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
            Assert.DoesNotContain(presented, update => update.Frame!.Pixels.Span[0] == 10);
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
                var snapshot = playing[index].Snapshot;
                Assert.True(snapshot.PresentedFrameTime <= snapshot.PresentedAtPosition);
                Assert.True(snapshot.PresentedFrameEnd > snapshot.PresentedAtPosition);
                Assert.InRange(snapshot.PreparedFrameCount, 0, 2);
                Assert.InRange(snapshot.PreparationPendingCount, 0, 2);
                Assert.InRange(snapshot.PreparedBytes, 0, 128L * 1024 * 1024);
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

    [Fact]
    public async Task AColdConversionExceedingTheLookaheadRecoversOnTheContinuouslyAdvancingSystemClock()
    {
        var source = new PreviewTestSource(10, Enumerable.Range(0, 200).Select(index => (long)index * 33).ToArray());
        var converter = new PreviewTestConverter
        {
            ConversionWork = marker => Thread.Sleep(marker == 10 ? 350 : 1)
        };
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, TimeProvider.System);
        await controller.OpenAsync("cold-conversion.mkv");
        await EventuallyAsync(() => HasFrame(updates, 10));
        await controller.PlayAsync();
        using var recoveryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (updates.Count(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING) < 12)
        {
            await Task.Delay(1, recoveryDeadline.Token);
        }

        var playing = updates.Where(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING).ToArray();
        Assert.True(converter.ConversionCount >= 13);
        Assert.All(playing, update =>
        {
            Assert.True(update.Snapshot.PresentedFrameTime <= update.Snapshot.PresentedAtPosition);
            Assert.True(update.Snapshot.PresentedAtPosition < update.Snapshot.PresentedFrameEnd);
            Assert.InRange(update.Snapshot.PreparedFrameCount, 0, 2);
            Assert.InRange(update.Snapshot.PreparationPendingCount, 0, 2);
        });
        Assert.True(controller.Snapshot.Position > new MediaTime(1, 4));
        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        AssertReleased(source, converter);
    }

    [Fact]
    public async Task ABlockedUiCallbackKeepsRefreshingItsExpiredQueuedSuccessorWithinTheTwoFrameBudget()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new PreviewTestSource(10, Enumerable.Range(0, 30).Select(index => index * 40L).ToArray());
        var converter = new PreviewTestConverter
        {
            ConversionWork = _ => clock.Advance(TimeSpan.FromMilliseconds(40))
        };
        var dispatcher = new PreviewTestDispatcher();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, clock, dispatcher);
        try
        {
            await controller.OpenAsync("refresh-queued-preview.mkv");
            await EventuallyAsync(() => HasFrame(updates, 10));
            converter.BlockNextConversion();
            await controller.PlayAsync();
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.BlockNextDispatch();
            converter.Release();
            await dispatcher.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => converter.ConversionCount >= 3 && controller.Snapshot.PreparedFrameCount == 2);

            for (var index = 0; index < 3; index++)
            {
                var previousConversions = converter.ConversionCount;
                clock.Advance(TimeSpan.FromMilliseconds(40));
                await EventuallyAsync(() => converter.ConversionCount > previousConversions && controller.Snapshot.PreparedFrameCount == 2);
                Assert.InRange(controller.Snapshot.PreparationPendingCount, 0, 2);
                Assert.InRange(controller.Snapshot.PreparedBytes, 0, 128L * 1024 * 1024);
                Assert.Single(updates, update => update.Frame is not null);
            }

            var position = controller.Snapshot.Position;
            dispatcher.RunPending();
            await EventuallyAsync(() => updates.Any(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING));
            var presented = updates.First(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING);
            Assert.Equal(position, presented.Snapshot.PresentedAtPosition);
            Assert.True(presented.Snapshot.PresentedFrameTime <= presented.Snapshot.PresentedAtPosition);
            Assert.True(presented.Snapshot.PresentedAtPosition < presented.Snapshot.PresentedFrameEnd);
            Assert.Equal(1, converter.MaximumActiveCount);
        }
        finally
        {
            converter.Release();
            dispatcher.RunPending();
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(0, controller.Snapshot.PreparedFrameCount);
        Assert.Equal(0, controller.Snapshot.PreparedBytes);
        Assert.Equal(0, controller.Snapshot.PreparationPendingCount);
        AssertReleased(source, converter);
    }

    [Fact]
    public async Task AQueuedExpiredFrameUsesItsAlreadyPreparedCurrentSuccessorInTheSameUiCallback()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new PreviewTestSource(10, 0, 40, 80, 120, 160, 200, 240, 280, 320);
        var converter = new PreviewTestConverter
        {
            ConversionWork = _ => clock.Advance(TimeSpan.FromMilliseconds(80))
        };
        var dispatcher = new PreviewTestDispatcher();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, clock, dispatcher);
        try
        {
            await controller.OpenAsync("late-bound-preview.mkv");
            await EventuallyAsync(() => HasFrame(updates, 10));
            converter.BlockNextConversion();
            await controller.PlayAsync();
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            dispatcher.BlockNextDispatch();
            converter.Release();
            await dispatcher.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await EventuallyAsync(() => converter.ConversionCount >= 3 && controller.Snapshot.PreparedFrameCount == 2);
            Assert.False(HasFrame(updates, 12));
            Assert.False(HasFrame(updates, 14));
            Assert.InRange(controller.Snapshot.PreparationPendingCount, 0, 2);
            Assert.InRange(controller.Snapshot.PreparedBytes, 0, 128L * 1024 * 1024);

            dispatcher.RunPending();
            await EventuallyAsync(() => HasFrame(updates, 14));
            var selected = updates.First(update => update.Frame?.Pixels.Span[0] == 14);
            Assert.Equal(new MediaTime(160, 1000), selected.Snapshot.PresentedFrameTime);
            Assert.Equal(new MediaTime(200, 1000), selected.Snapshot.PresentedFrameEnd);
            Assert.Equal(new MediaTime(160, 1000), selected.Snapshot.PresentedAtPosition);
            Assert.False(HasFrame(updates, 12));
            Assert.Equal(1, converter.MaximumActiveCount);
        }
        finally
        {
            converter.Release();
            dispatcher.RunPending();
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(0, controller.Snapshot.PreparedFrameCount);
        Assert.Equal(0, controller.Snapshot.PreparedBytes);
        Assert.Equal(0, controller.Snapshot.PreparationPendingCount);
        AssertReleased(source, converter);
    }

    [Fact]
    public async Task AFrameExpiringInsideTheUiQueueIsRejectedAndPlaybackRecovers()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 260);
        var converter = new PreviewTestConverter();
        var dispatcher = new PreviewTestDispatcher();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, clock, dispatcher);
        try
        {
            await controller.OpenAsync("late-ui.mkv");
            await EventuallyAsync(() => HasFrame(updates, 10));
            await controller.PlayAsync();
            dispatcher.BlockNextDispatch();
            await EventuallyAsync(() => clock.ActiveTimerCount != 0);
            clock.Advance(TimeSpan.FromMilliseconds(40));
            await dispatcher.Queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromMilliseconds(70));
            await EventuallyAsync(() => controller.Snapshot.PreparedFrameCount == 2);
            dispatcher.RunPending();
            await EventuallyAsync(() => HasFrame(updates, 12));
            var recovered = updates.First(update => update.Frame?.Pixels.Span[0] == 12);
            Assert.Equal(new MediaTime(110, 1000), recovered.Snapshot.PresentedAtPosition);
            clock.Advance(TimeSpan.FromMilliseconds(60));
            await EventuallyAsync(() => source.IssuedFrames.Any(frame => frame.Marker == 13));
            await EventuallyAsync(() => HasFrame(updates, 13));
            Assert.False(HasFrame(updates, 11));
            Assert.Equal(new MediaTime(170, 1000), controller.Snapshot.Position);
            Assert.All(updates.Where(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING), update =>
            {
                Assert.True(update.Snapshot.PresentedFrameTime <= update.Snapshot.PresentedAtPosition);
                Assert.True(update.Snapshot.PresentedAtPosition < update.Snapshot.PresentedFrameEnd);
            });
        }
        finally
        {
            dispatcher.RunPending();
        }
    }

    [Fact]
    public async Task AnEarlyConvertedFrameWaitsUntilItsRealPtsBeforePresentation()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new PreviewTestSource(10, 0, 40, 100, 160);
        var converter = new PreviewTestConverter
        {
            ConversionWork = marker =>
            {
                if (marker == 10)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(20));
                }
            }
        };
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, clock);
        await controller.OpenAsync("future.mkv");
        await EventuallyAsync(() => HasFrame(updates, 10));
        await controller.PlayAsync();
        await EventuallyAsync(() => clock.ActiveTimerCount != 0);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        await EventuallyAsync(() => converter.ConversionCount >= 2);
        Assert.False(HasFrame(updates, 11));
        Assert.Equal(new MediaTime(20, 1000), controller.Snapshot.Position);
        await EventuallyAsync(() => clock.ActiveTimerCount != 0);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        await EventuallyAsync(() => HasFrame(updates, 11));
        var presented = updates.First(update => update.Frame?.Pixels.Span[0] == 11);
        Assert.Equal(new MediaTime(40, 1000), presented.Snapshot.PresentedFrameTime);
        Assert.Equal(new MediaTime(100, 1000), presented.Snapshot.PresentedFrameEnd);
        Assert.Equal(new MediaTime(40, 1000), presented.Snapshot.PresentedAtPosition);
    }

    [Fact]
    public async Task SeekingCancelsAnAlreadyConvertedFutureFrameWithoutWaitingForItsDeadline()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new PreviewTestSource(10, 0, 400, 1000, 1200);
        var converter = new PreviewTestConverter
        {
            ConversionWork = marker =>
            {
                if (marker == 10)
                {
                    clock.Advance(TimeSpan.FromMilliseconds(20));
                }
            }
        };
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, converter, updates, clock);
        await controller.OpenAsync("cancel-ready-future.mkv");
        await EventuallyAsync(() => HasFrame(updates, 10));
        await controller.PlayAsync();
        await EventuallyAsync(() => clock.ActiveTimerCount != 0);
        clock.Advance(TimeSpan.FromMilliseconds(380));
        await EventuallyAsync(() => converter.ConversionCount >= 2);
        Assert.False(HasFrame(updates, 11));
        await controller.SeekAsync(new(100, 1000));
        await EventuallyAsync(() => controller.Snapshot.PresentedGeneration == 1);
        Assert.Equal(new MediaTime(100, 1000), controller.Snapshot.Position);
        Assert.Equal(new MediaTime(100, 1000), controller.Snapshot.PresentedAtPosition);
        Assert.Equal(MediaTime.Zero, controller.Snapshot.PresentedFrameTime);
        Assert.False(HasFrame(updates, 11));
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
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
