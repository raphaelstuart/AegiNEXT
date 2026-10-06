using AegiNext.Application;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class PreviewDecodeModeWorkflowTests
{
    [Fact]
    public async Task SelectionWithoutMediaPersistsWithoutChangingTheDocumentOrUndo()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        await using (var session = Create(directory.Path, editor))
        {
            await session.Styles.Completion;
            Assert.True(await session.SetPreviewDecodeModeAsync(VideoDecodeMode.Software));
            Assert.Equal(VideoDecodeMode.Software, session.Preferences.PreviewDecodeMode);
            Assert.Equal(VideoDecodeMode.Software, session.Controller.DecodeMode);
            Assert.Same(before, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(session.IsPreviewDecodeModeSwitching);
        }

        using var store = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(VideoDecodeMode.Software, store.Load().PreviewDecodeMode);
    }

    [Fact]
    public async Task PendingSwitchKeepsOtherPreferenceEditsAndRejectsARepeatedSelection()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        var converter = new PreviewTestConverter();
        converter.BlockNextConversion();
        var converterCount = 0;
        await using var session = new WorkbenchSession(new WorkspaceDialogStub(),
            update => new VideoPreviewController(Probe, (_, _, _, _) => new(_ => new PreviewTestSource(10, 0, 100, 200)),
                () => Interlocked.Increment(ref converterCount) == 2 ? converter : new PreviewTestConverter(), Dispatch, update),
            Dispatch, editor, new WorkbenchPreferencesStore(directory.Path));
        try
        {
            await session.Styles.Completion;
            await session.Controller.OpenAsync("pending.mp4");
            var switching = session.SetPreviewDecodeModeAsync(VideoDecodeMode.Hardware);
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.IsPreviewDecodeModeSwitching);
            Assert.False(await session.SetPreviewDecodeModeAsync(VideoDecodeMode.Software));
            session.UpdatePreferences(session.Preferences with { AccentColor = "#123456", Volume = 0.41F });
            converter.Release();
            Assert.True(await switching.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Equal(VideoDecodeMode.Hardware, session.Preferences.PreviewDecodeMode);
            Assert.Equal("#123456", session.Preferences.AccentColor);
            Assert.Equal(0.41F, session.Preferences.Volume);
            Assert.Equal(0.41F, session.Controller.Snapshot.Volume);
            Assert.False(session.IsPreviewDecodeModeSwitching);
            Assert.Same(before, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            converter.Release();
        }
    }

    [Fact]
    public async Task FailedHardwareSelectionRetainsPreferencesAndPublishesTheFailure()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        var failure = new NotSupportedException("No hardware decoder on this device.");
        await using var session = new WorkbenchSession(new WorkspaceDialogStub(),
            update => new VideoPreviewController(Probe, (_, _, _, options) => options.Mode == VideoDecodeMode.Hardware
                    ? throw failure
                    : new(_ => new PreviewTestSource(10, 0, 100, 200)),
                () => new PreviewTestConverter(), Dispatch, update),
            Dispatch, editor, new WorkbenchPreferencesStore(directory.Path));
        await session.Styles.Completion;
        await session.Controller.OpenAsync("failure.mp4");
        await session.Controller.SeekAsync(new(150, 1000));
        Assert.False(await session.SetPreviewDecodeModeAsync(VideoDecodeMode.Hardware).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(failure, session.LastError);
        Assert.Equal(VideoDecodeMode.Auto, session.Preferences.PreviewDecodeMode);
        Assert.Equal(VideoDecodeMode.Auto, session.Controller.DecodeMode);
        Assert.Equal(new MediaTime(150, 1000), session.Controller.Snapshot.Position);
        Assert.Equal(VideoPlaybackState.PAUSED, session.Controller.Snapshot.State);
        Assert.Contains("hardware", session.ViewModel.Error!, StringComparison.Ordinal);
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(session.IsPreviewDecodeModeSwitching);
    }

    private static WorkbenchSession Create(string directory, ProjectEditor editor)
    {
        return new(new WorkspaceDialogStub(),
            update => new VideoPreviewController(Probe, (_, _, _, _) => new(_ => new PreviewTestSource(10, 0, 100, 200)),
                () => new PreviewTestConverter(), Dispatch, update),
            Dispatch, editor, new WorkbenchPreferencesStore(directory));
    }

    private static Task<VideoPreviewMedia> Probe(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1)));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
