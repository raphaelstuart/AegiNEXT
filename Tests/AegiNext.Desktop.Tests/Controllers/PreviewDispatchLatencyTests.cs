using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Playback;
using Xunit.Abstractions;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class PreviewDispatchLatencyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AUiQueueSlowerThanOneFrameKeepsPresentingValidFramesOnTheSystemClock()
    {
        var source = new PreviewTestSource(10, Enumerable.Range(0, 200).Select(index => index * 33L).ToArray());
        var converter = new PreviewTestConverter();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(7))),
            (_, _) => new(_ => source), () => converter, DelayedDispatchAsync, updates.Enqueue);
        try
        {
            await controller.OpenAsync("ui-dispatch-latency.mkv");
            await WaitUntilAsync(() => updates.Any(update => update.Frame is not null), TimeSpan.FromSeconds(2));
            await controller.PlayAsync();
            try
            {
                await WaitUntilAsync(() => updates.Count(update => update.Frame is not null &&
                    update.Snapshot.State == VideoPlaybackState.PLAYING) >= 12, TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException($"Expected 12 PLAYING frames within two seconds, actual " +
                    $"{updates.Count(update => update.Frame is not null && update.Snapshot.State == VideoPlaybackState.PLAYING)}. " +
                    $"Snapshot={controller.Snapshot}; {controller.PipelineDiagnostics}; {PreviewThreadPoolDiagnostics.Read()}");
            }

            var playing = updates.Where(update => update.Frame is not null &&
                update.Snapshot.State == VideoPlaybackState.PLAYING).ToArray();
            Assert.All(playing, update =>
            {
                Assert.True(update.Snapshot.PresentedFrameTime <= update.Snapshot.PresentedAtPosition);
                Assert.True(update.Snapshot.PresentedAtPosition < update.Snapshot.PresentedFrameEnd);
                Assert.InRange(update.Snapshot.PreparedFrameCount, 0, 2);
                Assert.InRange(update.Snapshot.PreparationPendingCount, 0, 2);
            });
            Assert.True(playing[^1].Snapshot.PresentedFrameTime > playing[0].Snapshot.PresentedFrameTime);
            Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
            output.WriteLine($"PLAYING={playing.Length}; {controller.PipelineDiagnostics}; {PreviewThreadPoolDiagnostics.Read()}");
        }
        finally
        {
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, converter.DisposeCount);
        Assert.All(source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    private static async Task DelayedDispatchAsync(Action action, CancellationToken cancellationToken)
    {
        await Task.Delay(40, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        action();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(1, cancellation.Token);
        }
    }
}
