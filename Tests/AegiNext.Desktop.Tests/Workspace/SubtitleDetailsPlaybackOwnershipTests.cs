using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Tests.Controllers;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsPlaybackOwnershipTests
{
    [Fact]
    public async Task ReplacedDetailsPlaybackCannotStopOrLoopNewAudioAudition()
    {
        var line = new SubtitleLine { Text = "first", Start = new(1, 10), End = new(2, 10) };
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500, 800);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        await using var context = new WorkspaceSessionTestContext(document,
            controllerFactory: update => VideoPreviewAudioRangeTests.CreateController(source, audioSource, output, update));
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("details.mkv");
        context.Session.SelectCue(line.Id);
        await context.Session.Details.PlayAsync(false, true);
        Assert.True(context.Session.Details.IsPlaying);
        using var auditionOwner = new CancellationTokenSource();
        await context.Session.Controller.PlayAudioRangeAsync(new(1, 2), new(3, 4), auditionOwner.Token);

        Assert.False(context.Session.Details.IsPlaying);
        await context.Session.Details.SetLoopEnabledAsync(true);
        await context.Session.Details.StopPlaybackAsync();

        Assert.True(context.Session.Controller.IsPlaybackRangeOwnedBy(auditionOwner.Token));
        Assert.False(output.Paused);
        Assert.Equal(new MediaTime(1, 2), audioSource.LastSeek);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        output.Consume(48000);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (context.Session.Controller.IsRangePlaybackActive || !output.Paused)
        {
            await Task.Delay(5, timeout.Token);
            output.Consume(48000);
        }
        Assert.Equal(new MediaTime(1, 2), audioSource.LastSeek);
        Assert.Null(context.Session.Controller.Snapshot.Error);
    }
}
