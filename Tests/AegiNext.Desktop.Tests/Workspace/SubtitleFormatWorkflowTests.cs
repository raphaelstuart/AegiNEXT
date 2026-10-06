using AegiNext.Desktop.Shortcuts;
using System.Text;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleFormatWorkflowTests
{
    [Fact]
    public async Task SrtImportCreatesIndependentTracksForOverlapsAndOneUndoRestoresEverything()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "overlap.srt");
        await File.WriteAllTextAsync(path, "1\n00:00:00,000 --> 00:00:02,000\none\n\n2\n00:00:01,000 --> 00:00:03,000\ntwo\n");
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.Null(context.Session.LastError);
        Assert.Equal(3, context.Editor.Snapshot.SubtitleTracks.Length);
        Assert.Equal(2, context.Editor.Snapshot.Subtitles.Length);
        Assert.Equal(2, context.Editor.Snapshot.Layers.Length);
        Assert.DoesNotContain(context.Editor.Snapshot.Subtitles, line => line.TrackId == original.SubtitleTracks[0].Id);
        Assert.NotNull(context.Session.SelectedCueId);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task AssUnsupportedMoveRequiresExplicitConversionAndCancelLeavesSnapshotUntouched()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "move.ass");
        await File.WriteAllTextAsync(path, "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\move(0,0,100,100)}hello\n");
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);
        Assert.Null(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Contains(context.Dialogs.ConversionDiagnostics, message => message.Contains("move", StringComparison.OrdinalIgnoreCase));
        context.Dialogs.ConversionChoice = true;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);
        Assert.Equal("hello", Assert.Single(context.Editor.Snapshot.Subtitles).Text);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task Utf16BomIsRejectedWhileUtf8BomIsAccepted()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "encoding.srt");
        const string TEXT = "1\n00:00:00,000 --> 00:00:02,000\n中文\n";
        await File.WriteAllTextAsync(path, TEXT, Encoding.Unicode);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.NotNull(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        await File.WriteAllTextAsync(path, TEXT, new UTF8Encoding(true));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.Null(context.Session.LastError);
        Assert.Equal("中文", Assert.Single(context.Editor.Snapshot.Subtitles).Text);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task InvalidUtf8RejectsImportAtomically()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "invalid.srt");
        await File.WriteAllBytesAsync(path, [0xC3, 0x28]);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.NotNull(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
