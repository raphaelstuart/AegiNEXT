using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Playback;
using Xunit.Abstractions;

namespace AegiNext.Desktop.Tests;

public sealed class PlaybackPreparationLatencyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DecoderConversionAndUiQueueLatencyKeepPresentingValidSixtyFpsIntervalsOnTheSystemClock()
    {
        var source = new LatencyPreviewSource(360, 60, TimeSpan.FromMilliseconds(8));
        var converter = new PreviewTestConverter
        {
            ConversionWork = _ => Thread.Sleep(1)
        };
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        var firstPausedFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var playingFrameCount = 0;
        await using var controller = new VideoPreviewController((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1)));
        }, (_, _) => new(_ => source, TimeProvider.System), () => converter,
            async (action, token) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), token);
                action();
            }, update =>
            {
                updates.Enqueue(update);
                if (update.Frame is not null)
                {
                    if (update.Snapshot.State == VideoPlaybackState.PAUSED)
                    {
                        firstPausedFrame.TrySetResult();
                    }
                    else if (update.Snapshot.State == VideoPlaybackState.PLAYING)
                    {
                        Interlocked.Increment(ref playingFrameCount);
                    }
                }
            });

        await controller.OpenAsync("sixty-fps-decoder-ui-latency.mkv");
        await firstPausedFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.PlayAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sustainedDiagnostics = "Sustained playback measurement has not started.";
        try
        {
            while (Volatile.Read(ref playingFrameCount) < 12)
            {
                await Task.Delay(1, deadline.Token);
            }

            var firstSustainedCount = Volatile.Read(ref playingFrameCount);
            var firstTimestampBefore = TimeProvider.System.GetTimestamp();
            var firstSustainedPosition = controller.Snapshot.Position;
            var firstTimestampAfter = TimeProvider.System.GetTimestamp();
            await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            var timerTimestampBefore = TimeProvider.System.GetTimestamp();
            var sustainedSnapshot = controller.Snapshot;
            var timerTimestampAfter = TimeProvider.System.GetTimestamp();
            var minimumElapsed = new MediaTime(timerTimestampBefore - firstTimestampAfter, TimeProvider.System.TimestampFrequency);
            var maximumElapsed = new MediaTime(timerTimestampAfter - firstTimestampBefore, TimeProvider.System.TimestampFrequency);
            var actualDelta = sustainedSnapshot.Position - firstSustainedPosition;
            sustainedDiagnostics = $"TimerWake: ActualDelta={actualDelta}; Elapsed=[{minimumElapsed}, {maximumElapsed}]; " +
                $"SystemPosition=[{firstSustainedPosition + minimumElapsed}, {firstSustainedPosition + maximumElapsed}]; " +
                $"ActualPosition={sustainedSnapshot.Position}; PLAYING={Volatile.Read(ref playingFrameCount)}";
            output.WriteLine(sustainedDiagnostics);
            Assert.True(actualDelta >= minimumElapsed && actualDelta <= maximumElapsed, sustainedDiagnostics);

            var requiredPosition = firstSustainedPosition + new MediaTime(1, 4);
            while (sustainedSnapshot.Position < requiredPosition || Volatile.Read(ref playingFrameCount) < firstSustainedCount + 4)
            {
                await Task.Delay(1, deadline.Token);
                var nextSnapshot = controller.Snapshot;
                Assert.True(nextSnapshot.Position >= sustainedSnapshot.Position,
                    $"System playback position regressed from {sustainedSnapshot.Position} to {nextSnapshot.Position}; {sustainedDiagnostics}");
                sustainedSnapshot = nextSnapshot;
            }
            Assert.True(sustainedSnapshot.Position >= requiredPosition,
                $"ActualDelta={sustainedSnapshot.Position - firstSustainedPosition}; RequiredDelta={new MediaTime(1, 4)}; {sustainedDiagnostics}");
            output.WriteLine($"Sustained: ActualDelta={sustainedSnapshot.Position - firstSustainedPosition}; " +
                $"Elapsed={new MediaTime(TimeProvider.System.GetTimestamp() - firstTimestampBefore, TimeProvider.System.TimestampFrequency)}; " +
                $"NewPLAYING={Volatile.Read(ref playingFrameCount) - firstSustainedCount}");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException($"Expected 12 then four more PLAYING frames within two seconds, actual " +
                $"{Volatile.Read(ref playingFrameCount)}; ReadCount={source.ReadCount}; SeekCount={source.SeekCount}; " +
                $"{sustainedDiagnostics}; Snapshot={controller.Snapshot}; {controller.PipelineDiagnostics}; {PreviewThreadPoolDiagnostics.Read()}");
        }

        var snapshot = controller.Snapshot;
        Assert.Null(snapshot.Error);
        Assert.Equal(VideoPlaybackState.PLAYING, snapshot.State);
        Assert.True(snapshot.Position < new MediaTime(359, 60));
        Assert.True(source.ReadCount >= 12);
        var playing = updates.Where(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING).ToArray();
        Assert.True(playing.Length >= 16);
        Assert.All(playing, update =>
        {
            Assert.NotNull(update.Snapshot.PresentedFrameTime);
            Assert.NotNull(update.Snapshot.PresentedFrameEnd);
            Assert.True(update.Snapshot.PresentedFrameTime <= update.Snapshot.PresentedAtPosition);
            Assert.True(update.Snapshot.PresentedAtPosition < update.Snapshot.PresentedFrameEnd);
            Assert.Equal(new MediaTime(1, 60), update.Snapshot.PresentedFrameEnd!.Value - update.Snapshot.PresentedFrameTime!.Value);
            Assert.InRange(update.Snapshot.PreparedFrameCount, 0, 2);
            Assert.InRange(update.Snapshot.PreparationPendingCount, 0, 2);
            Assert.InRange(update.Snapshot.PreparedBytes, 0, 128L * 1024 * 1024);
        });
        for (var index = 1; index < playing.Length; index++)
        {
            Assert.True(playing[index].Snapshot.PresentedFrameTime > playing[index - 1].Snapshot.PresentedFrameTime);
        }
        output.WriteLine($"PLAYING={playing.Length}; ReadCount={source.ReadCount}; SeekCount={source.SeekCount}; " +
            $"{controller.PipelineDiagnostics}; {PreviewThreadPoolDiagnostics.Read()}");

        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, converter.DisposeCount);
        Assert.Equal(1, converter.MaximumActiveCount);
        Assert.All(source.IssuedFrames, frame =>
        {
            Assert.Equal(1, frame.DisposeCount);
            Assert.Equal(new MediaTimeBase(1, 60), frame.Info.TimeBase);
        });
        Assert.Null(controller.Snapshot.PresentedFrameTime);
        Assert.Equal(0, controller.Snapshot.PreparedFrameCount);
        Assert.Equal(0, controller.Snapshot.PreparationPendingCount);
    }
}
