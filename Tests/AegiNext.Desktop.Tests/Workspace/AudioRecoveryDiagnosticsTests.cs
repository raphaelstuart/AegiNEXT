using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AudioRecoveryDiagnosticsTests
{
    /// <summary>设备恢复期间较早的 UI Tick 不能在恢复完成后重新显示已经失效的输出错误。</summary>
    [Fact]
    public async Task ACompletedOutputRecoveryCannotRestoreAnErrorFromAnEarlierTickSnapshot()
    {
        var firstOutput = new CalibrationAudioOutput("speaker");
        var nextOutput = new CalibrationAudioOutput("headphones");
        var enteredFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opens = 0;
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 1000, 2000, 5000, 10000), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            async (_, _, position, token) =>
            {
                if (Interlocked.Increment(ref opens) == 1)
                {
                    return new(new CalibrationAudioSource(), firstOutput, position);
                }
                enteredFactory.TrySetResult();
                await releaseFactory.Task.WaitAsync(token);
                return new(new CalibrationAudioSource(), nextOutput, position);
            }));
        Task? oldTick = null;
        try
        {
            await context.InitializeAsync();
            var session = context.Session;
            await session.Controller.OpenAsync("recover-diagnostic.mkv");
            await session.Controller.PlayAsync();
            firstOutput.Quality = AudioClockQuality.UNAVAILABLE;
            session.Tick();
            await enteredFactory.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(session.Controller.Snapshot.AudioError);
            session.ViewModel.Preview.FileTitle = "Force an earlier snapshot across output recovery";
            session.ViewModel.Preview.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == "FileTitle")
                {
                    capturedTick.TrySetResult();
                    releaseTick.Task.GetAwaiter().GetResult();
                }
            };
            oldTick = Task.Run(session.Tick);
            await capturedTick.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseFactory.TrySetResult();
            await session.AudioCalibrationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(session.Controller.Snapshot.AudioError);
            releaseTick.TrySetResult();
            await oldTick.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(session.LastError);
            Assert.Null(session.ViewModel.Error);
            Assert.Equal(AudioClockQuality.SYSTEM, session.AudioClock!.Quality);
            Assert.Equal(2, opens);
        }
        finally
        {
            releaseFactory.TrySetResult();
            releaseTick.TrySetResult();
            if (oldTick is not null)
            {
                await oldTick.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
