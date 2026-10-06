using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkbenchPlaybackRangeResumeTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task PlayPauseAfterRangeEndResumesThereUnlessTheActualMediaEndWasReached(bool atMediaEnd, bool knownDuration)
    {
        var source = new PreviewTestSource(10, 3000, 3040, 3100, 3600, 3990);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(3), knownDuration ? new MediaTime(1) : null, 1)),
            (_, _, position) => new(_ => source, clock, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(audioSource, output, position))));
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("range-end.mkv");
        var end = atMediaEnd ? new MediaTime(4) : new MediaTime(31, 10);
        var start = atMediaEnd ? new MediaTime(399, 100) : new MediaTime(304, 100);
        await context.Session.Controller.PlayRangeAsync(start, end, false);
        output.Consume(48000);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (context.Session.Controller.Snapshot.State != VideoPlaybackState.ENDED ||
            context.Session.Controller.IsRangePlaybackActive || !output.Paused)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(5, timeout.Token);
        }
        Assert.Equal(end, context.Session.Controller.Snapshot.Position);
        Assert.True(context.Session.Controller.Snapshot.PlaybackRangeInstalled);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);

        var expected = atMediaEnd ? new MediaTime(3) : end;
        Assert.Equal(VideoPlaybackState.PLAYING, context.Session.Controller.Snapshot.State);
        Assert.Equal(expected, context.Session.Controller.Snapshot.Position);
        Assert.Equal(expected, audioSource.LastSeek);
        Assert.Equal(expected - new MediaTime(3), context.Session.ProjectPosition);
        Assert.False(context.Session.Controller.Snapshot.PlaybackRangeInstalled);
        Assert.False(context.Session.Controller.IsRangePlaybackActive);
        Assert.False(output.Paused);
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
