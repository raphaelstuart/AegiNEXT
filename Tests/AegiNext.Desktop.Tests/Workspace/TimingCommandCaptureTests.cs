using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimingCommandCaptureTests
{
    [Fact]
    public async Task TimingEnterCapturesTheProjectClockBeforeCommittingWorkspaceDrafts()
    {
        var clock = new ManualPlaybackTimeProvider();
        var original = new SubtitleLine { Start = new(10), End = new(12), Text = "original" };
        await using var context = new WorkspaceSessionTestContext(CreateDocument("timing-capture.mkv", original),
            controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(3), new(20))),
            (_, _) => new(_ => new PreviewTestSource(10, 3000, 3040, 4000, 5000, 16000), clock),
            () => new PreviewTestConverter(), Dispatch, update));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("timing-capture.mkv");
        await session.Controller.SeekAsync(new(4));
        await session.Controller.PlayAsync();
        session.ViewModel.Subtitles.Rows.Single(row => row.Id == original.Id).Text = "draft 中文 ABC 123";
        var advanced = false;
        context.Editor.Changed += AdvanceDuringDraftCommit;
        try
        {
            await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
            Assert.True(advanced);
            Assert.Equal(new MediaTime(1), context.Editor.Snapshot.Subtitles.Single(line => line.Id != original.Id).Start);
            Assert.Equal(new MediaTime(5, 4), session.ProjectPosition);
        }
        finally
        {
            context.Editor.Changed -= AdvanceDuringDraftCommit;
        }

        return;

        void AdvanceDuringDraftCommit(object? sender, EventArgs args)
        {
            if (!advanced && context.Editor.Snapshot.Subtitles.Single(line => line.Id == original.Id).Text == "draft 中文 ABC 123")
            {
                advanced = true;
                clock.Advance(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    [Fact]
    public async Task TimingExitUsesTheClockCapturedBeforeCommandLoggingAndDraftPreparation()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(CreateDocument("timing-exit-capture.mkv"),
            controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(3), new(20))),
            (_, _) => new(_ => new PreviewTestSource(10, 3000, 3040, 4000, 5000, 16000), clock),
            () => new PreviewTestConverter(), Dispatch, update));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("timing-exit-capture.mkv");
        await session.Controller.SeekAsync(new(4));
        await session.Controller.PlayAsync();
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        var entered = Assert.Single(context.Editor.Snapshot.Subtitles);
        Assert.Equal(new MediaTime(1), entered.Start);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        var advanced = false;
        session.Journal.Changed += AdvanceDuringLogging;
        try
        {
            await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_EXIT);
            Assert.True(advanced);
            Assert.Equal(new MediaTime(13, 10), Assert.Single(context.Editor.Snapshot.Subtitles).End);
            Assert.Equal(new MediaTime(31, 20), session.ProjectPosition);
        }
        finally
        {
            session.Journal.Changed -= AdvanceDuringLogging;
        }

        return;

        void AdvanceDuringLogging(object? sender, EventArgs args)
        {
            if (!advanced && session.Journal.Entries[^1].Message == WorkbenchCommand.TIMING_EXIT.ToString())
            {
                advanced = true;
                clock.Advance(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    [Fact]
    public async Task PausedExactTargetAndTimingCommandsUseTheSystemAudioClockInsteadOfTheDisplayedPts()
    {
        var clock = new ManualPlaybackTimeProvider();
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var context = new WorkspaceSessionTestContext(CreateDocument("system-clock-exact-pause.mkv", audioIndex: 1),
            controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(3), new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 3000, 3033, 3099, 3200, 16000), clock, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(new PreviewAudioSource(), output, position))));
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("system-clock-exact-pause.mkv");
        await session.Controller.PlayAsync();
        output.Consume(2400);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(new MediaTime(61, 20), session.Controller.Snapshot.Position);
        await session.Controller.PauseAsync();
        await EventuallyAsync(() => session.Controller.Snapshot.PresentedFrameTime == new MediaTime(3033, 1000));
        Assert.Equal(new MediaTime(61, 20), session.Controller.Snapshot.PresentedAtPosition);
        output.Consume(9600);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(new MediaTime(61, 20), session.Controller.Snapshot.Position);
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
        Assert.Equal(new MediaTime(1, 20), Assert.Single(context.Editor.Snapshot.Subtitles).Start);
        await session.Controller.PlayAsync();
        output.Consume(1200);
        clock.Advance(TimeSpan.FromSeconds(5));
        await session.ExecuteCommandAsync(WorkbenchCommand.TIMING_EXIT);
        Assert.Equal(new MediaTime(3, 40), Assert.Single(context.Editor.Snapshot.Subtitles).End);
    }

    private static ProjectDocument CreateDocument(string path, SubtitleLine? line = null, int? audioIndex = null)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: Path.GetFullPath(path));
        return new()
        {
            Assets = [asset],
            Media = new(asset.Id, 0, audioIndex, new(3)) { PlaybackOrigin = new(3) },
            Subtitles = line is null ? [] : [line],
            Layers = line is null ? [] : [new()
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }]
        };
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
