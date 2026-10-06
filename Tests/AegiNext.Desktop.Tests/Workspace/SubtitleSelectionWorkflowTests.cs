using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleSelectionWorkflowTests
{
    private static readonly string[] subtitleTexts = ["First", "Second", "Third"];

    [Fact]
    public async Task BatchMergeUsesActualSelectionAndOneUndoRestoresAllThreeClips()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Subtitles.Select(line => line.Id).ToArray();
        Assert.True(session.SelectSubtitleRows(ids[2], ids.Reverse()));

        await session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.MERGE_SUBTITLE);

        var merged = context.Editor.Snapshot;
        Assert.Equal("First\nSecond\nThird", Assert.Single(merged.Subtitles).Text);
        Assert.Equal(ids[0], session.SelectedCueId);
        Assert.Equal(new[] { ids[0] }, session.SelectedSubtitleIds);
        Assert.Equal("Merge subtitles", context.Editor.UndoLabel);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(merged, context.Editor.Snapshot);
    }

    [Fact]
    public async Task RefreshRetainsSelectionExternalPrimaryCollapsesItAndTrackSwitchClearsIt()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Subtitles.Select(line => line.Id).ToArray();
        Assert.True(session.SelectSubtitleRows(ids[0], ids[..2]));
        session.RefreshDocument();
        Assert.Equal(ids[..2], session.SelectedSubtitleIds);

        session.SelectCue(ids[0]);
        Assert.Equal(new[] { ids[0] }, session.SelectedSubtitleIds);
        session.SelectLayer(document.Layers[1].Id, [document.Layers[0].Id, document.Layers[1].Id]);
        Assert.Equal(ids[..2], session.SelectedSubtitleIds);
        Assert.Equal(ids[1], session.SelectedCueId);

        var track = context.Editor.AddSubtitleTrack("Other");
        Assert.True(session.SelectTrack(track));
        Assert.Empty(session.SelectedSubtitleIds);
        Assert.Null(session.SelectedCueId);
        Assert.Empty(session.ViewModel.Subtitles.VisibleRows);
        session.ResetSelection();
        session.RefreshDocument();
        Assert.Empty(session.SelectedSubtitleIds);
    }

    [Fact]
    public async Task SingleLastSubtitleDisablesMergeAndSingleFirstStillMergesWithNext()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(document.Subtitles[^1].Id);
        Assert.False(session.CanExecuteCommand(WorkbenchCommand.MERGE_SUBTITLE));
        await session.ExecuteCommandAsync(WorkbenchCommand.MERGE_SUBTITLE);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        session.SelectCue(document.Subtitles[0].Id);
        Assert.True(session.CanExecuteCommand(WorkbenchCommand.MERGE_SUBTITLE));
        await session.ExecuteCommandAsync(WorkbenchCommand.MERGE_SUBTITLE);
        Assert.Equal(2, context.Editor.Snapshot.Subtitles.Length);
        Assert.Equal("First\nSecond", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal("Third", context.Editor.Snapshot.Subtitles[1].Text);
    }

    [Fact]
    public async Task InvalidDraftRejectsSelectionChangeWithoutLosingTheDraftOrHistory()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Subtitles.Select(line => line.Id).ToArray();
        Assert.True(session.SelectSubtitleRows(ids[0], ids[..2]));
        var row = session.ViewModel.Subtitles.Rows[0];
        row.StartText = "invalid";

        Assert.False(session.SelectSubtitleRows(ids[2], [ids[2]]));

        Assert.Equal(ids[..2], session.SelectedSubtitleIds);
        Assert.Equal(ids[0], session.SelectedCueId);
        Assert.Equal("invalid", row.StartText);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        row.Accept(document.Subtitles[0]);
    }

    private static ProjectDocument CreateDocument()
    {
        var lines = subtitleTexts.Select((text, index) => new SubtitleLine
        {
            Text = text, Start = new(index * 3), End = new(index * 3 + 2),
            Style = new() { FontFamily = "sans-serif", FontSize = 24 }
        }).ToImmutableArray();
        return new()
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
    }
}
