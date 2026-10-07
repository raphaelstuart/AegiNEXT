using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>展示回调不能占用播放控制状态锁，资源在回调完成后回收。</summary>
public sealed class VideoPreviewPresentationOwnershipTests
{
    /// <summary>慢画布展示仍允许其他线程读取和受理暂停状态。</summary>
    [Fact]
    public async Task ASlowPresentationDoesNotBlockSnapshotReadersOrPauseAcceptance()
    {
        var source = new PreviewTestSource(10, 0, 1000, 2000, 3000, 4000, 5000);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        var block = 0;
        var initialFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(6))),
            (_, _) => new(_ => source), () => new PreviewTestConverter(), Dispatch, update =>
            {
                if (update.Frame is null)
                {
                    return;
                }
                initialFrame.TrySetResult();
                if (Interlocked.Exchange(ref block, 0) != 0)
                {
                    entered.TrySetResult();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                }
            });
        await controller.OpenAsync("slow-present.mkv");
        await initialFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Write(ref block, 1);
        var seek = controller.SeekAsync(new(2));
        Task? pause = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = await Task.Run(() => controller.Snapshot).WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(VideoPlaybackState.PAUSED, snapshot.State);
            var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pause = Task.Run(async () =>
            {
                var operation = controller.PauseAsync();
                accepted.TrySetResult();
                await operation;
            });
            await accepted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            try
            {
                await seek.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
            if (pause is not null)
            {
                await pause.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, source.DisposeCount);
        Assert.All(source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
