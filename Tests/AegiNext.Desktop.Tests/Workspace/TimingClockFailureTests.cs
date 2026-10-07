using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimingClockFailureTests
{
    [Fact]
    public async Task MissingAudioOutputDisablesTimingCommandsInsteadOfUsingTheVideoFallbackClock()
    {
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 1000, 2000, 16000), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, _, _) => Task.FromException<AudioPlaybackSession>(new IOException("No audio device"))));
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("missing-audio-clock.mkv");
        await context.Session.Controller.PlayAsync();
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_ENTER));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        Assert.Empty(context.Editor.Snapshot.Subtitles);
    }

    [Fact]
    public async Task UnavailableAudioClockFreezesPlaybackPositionAndDisablesBothTimingCommands()
    {
        var clock = new ManualPlaybackTimeProvider();
        var output = new PreviewAudioOutput();
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 1000, 2000, 16000), clock, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(new PreviewAudioSource(), output, position))));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("audio-clock-loss.mkv");
        await session.Controller.PlayAsync();
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        Assert.Single(context.Editor.Snapshot.Subtitles);
        var before = context.Editor.Snapshot;
        var frozen = session.Controller.Snapshot.Position;
        output.ClockQuality = AudioClockQuality.UNAVAILABLE;
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.TIMING_ENTER));
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
        Assert.NotNull(session.Controller.Snapshot.AudioError);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(frozen, session.Controller.Snapshot.Position);
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_EXIT);
        Assert.Same(before, context.Editor.Snapshot);
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
