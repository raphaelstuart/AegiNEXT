using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Tests.Controllers;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TransportSeekingWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserAbsoluteProjectAndRelativeSeeksPreservePlaybackWhileEditingSeeksPause(bool playing)
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = CreateContext(clock, new PreviewTestSource(10, 3000, 4000, 6000, 7000, 10000, 14000, 23000));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("transport-seek.mkv");
        await session.Controller.SeekAsync(new(4));
        if (playing)
        {
            await session.Controller.PlayAsync();
        }

        await session.SeekFromUserAsync(new(6));
        AssertPlayback(session.Controller, new(6), playing);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        AssertPlayback(session.Controller, playing ? new(31, 5) : new(6), playing);

        await session.SeekProjectTimeAsync(new(4));
        AssertPlayback(session.Controller, new(7), playing);
        await session.SeekRelativeAsync(5);
        AssertPlayback(session.Controller, new(12), playing);

        await session.SeekForEditingAsync(new(1));
        AssertPlayback(session.Controller, new(4), false);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        AssertPlayback(session.Controller, new(4), false);
    }

    [Fact]
    public async Task ALaterTransportSeekRetainsThePlayingIntentWhileTheEarlierSeekHasTemporarilyPaused()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new BlockingPreviewSeekSource(new(10, 3000, 4000, 6000, 7000, 10000, 23000));
        await using var context = CreateContext(clock, source);
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("transport-in-flight.mkv");
        await session.Controller.PlayAsync();
        source.BlockNextSeek();
        try
        {
            var first = session.SeekFromUserAsync(new(6));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var latest = session.SeekProjectTimeAsync(new(4));
            source.Release();
            await Task.WhenAll(first, latest).WaitAsync(TimeSpan.FromSeconds(5));
            AssertPlayback(session.Controller, new(7), true);
            clock.Advance(TimeSpan.FromMilliseconds(200));
            AssertPlayback(session.Controller, new(36, 5), true);
        }
        finally
        {
            source.Release();
        }
    }

    private static WorkspaceSessionTestContext CreateContext(ManualPlaybackTimeProvider clock,
        IVideoFrameSource source)
    {
        return new(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(3), new(20))),
            (_, _) => new(_ => source, clock), () => new PreviewTestConverter(), Dispatch, update));
    }

    private static void AssertPlayback(VideoPreviewController controller, MediaTime position, bool playing)
    {
        Assert.Equal(playing ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(position, controller.Snapshot.Position);
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
