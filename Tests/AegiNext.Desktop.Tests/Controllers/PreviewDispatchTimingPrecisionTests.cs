using System.Collections.Concurrent;
using System.Threading.Channels;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>连续交付的成本反馈保持有界精度，并持续呈现有效视频区间。</summary>
public sealed class PreviewDispatchTimingPrecisionTests
{
    /// <summary>四毫秒交付和超过一毫秒的定时器迟到经过数千帧和七十秒媒体时间后仍可持续播放。</summary>
    [Fact]
    public async Task PlaybackPastSeventySecondsKeepsPresentingValidFramesWithNonIntegralDelays()
    {
        var clock = new ObservablePreviewTimeProvider();
        var source = new LatencyPreviewSource(9000, 60, TimeSpan.Zero);
        var converter = new PreviewTestConverter();
        var presentations = new ConcurrentQueue<VideoPreviewSnapshot>();
        var completedDispatches = Channel.CreateUnbounded<bool>();
        var firstPausedFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new VideoPreviewController(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(150)));
            }, (_, _) => new(_ => source, clock), () => converter,
            (action, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(TimeSpan.FromTicks(40001));
                action();
                completedDispatches.Writer.TryWrite(true);
                return Task.CompletedTask;
            }, update =>
            {
                if (update.Snapshot.Error is { } error)
                {
                    firstFailure.TrySetResult(error);
                }
                if (update.Frame is null)
                {
                    return;
                }
                if (update.Snapshot.State == VideoPlaybackState.PAUSED)
                {
                    firstPausedFrame.TrySetResult();
                }
                else if (update.Snapshot.State == VideoPlaybackState.PLAYING)
                {
                    presentations.Enqueue(update.Snapshot);
                }
            });
        try
        {
            await controller.OpenAsync("dispatch-feedback-precision.mkv");
            await firstPausedFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await controller.PlayAsync();
            while (completedDispatches.Reader.TryRead(out _))
            {
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var observedWaitFeedback = false;
            try
            {
                while (presentations.Count < 300 ||
                    controller.Snapshot.PresentedFrameTime is not { } presentedTime || presentedTime < new MediaTime(70))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    Assert.Null(controller.Snapshot.Error);
                    while (completedDispatches.Reader.TryRead(out _))
                    {
                        observedWaitFeedback |= controller.PipelineDiagnostics.Contains("Stage = dispatch-wait", StringComparison.Ordinal);
                    }
                    var revision = clock.ScheduleRevision;
                    clock.AdvanceToNextTimer(TimeSpan.FromTicks(10001));
                    if (clock.HasScheduledTimer)
                    {
                        await Task.Yield();
                        continue;
                    }
                    var scheduled = clock.WaitForScheduleChangeAsync(revision);
                    var dispatched = completedDispatches.Reader.WaitToReadAsync(deadline.Token).AsTask();
                    await Task.WhenAny(scheduled, dispatched, firstFailure.Task).WaitAsync(deadline.Token);
                    if (firstFailure.Task.IsCompleted)
                    {
                        Assert.Null(await firstFailure.Task);
                    }
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                throw new Xunit.Sdk.XunitException($"Expected 300 PLAYING frames and 70 seconds on the manual clock; " +
                    $"actual={presentations.Count}; Snapshot={controller.Snapshot}; " +
                    $"{clock.Diagnostics}; {controller.PipelineDiagnostics}");
            }

            var playing = presentations.ToArray();
            Assert.True(playing.Length >= 300);
            Assert.Null(controller.Snapshot.Error);
            Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
            Assert.True(playing[^1].PresentedFrameTime >= new MediaTime(70));
            Assert.True(observedWaitFeedback || controller.PipelineDiagnostics.Contains("Stage = dispatch-wait", StringComparison.Ordinal));
            Assert.All(playing, snapshot =>
            {
                Assert.NotNull(snapshot.PresentedFrameTime);
                Assert.NotNull(snapshot.PresentedFrameEnd);
                Assert.True(snapshot.PresentedFrameTime <= snapshot.PresentedAtPosition);
                Assert.True(snapshot.PresentedAtPosition < snapshot.PresentedFrameEnd);
                Assert.Equal(new MediaTime(1, 60), snapshot.PresentedFrameEnd!.Value - snapshot.PresentedFrameTime!.Value);
            });
            for (var index = 1; index < playing.Length; index++)
            {
                Assert.True(playing[index].PresentedFrameTime > playing[index - 1].PresentedFrameTime);
            }
        }
        finally
        {
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, converter.DisposeCount);
        Assert.All(source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }
}
