using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests;

public sealed class VideoPreviewRunTests
{
    [Fact]
    public async Task StoppingDoesNotHoldTheClockOwnerLockWhileInvokingCancellationCallbacks()
    {
        using var run = new VideoPreviewRun(1, "stop-clock-lock.mkv");
        await using var audio = new AudioPlaybackSession(new PreviewAudioSource(), new PreviewAudioOutput(), MediaTime.Zero);
        run.AttachAudio(audio);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = run.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });
        var stopping = Task.Run(run.Stop);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(audio, await Task.Run(() => run.Audio).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(run.Stop(), run.Stop());
            Assert.ThrowsAny<OperationCanceledException>(() => run.AttachAudio(audio));
        }
        finally
        {
            release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
