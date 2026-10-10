using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsMultiSelectionTests
{
    [Fact]
    public async Task MultiMergePreservesWholeTimingAndRangeAppearanceWithPendingTextInOneUndo()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        var ids = line.Karaoke.Take(3).Select(clip => clip.Id).ToArray();
        Assert.True(details.SelectClips(ids.Reverse(), ids[1]));
        Assert.Equal(ids, details.SelectedClipIds);
        Assert.Equal(ids[1], details.SelectedClipId);
        Assert.False(context.Editor.CanUndo);
        Assert.Null(details.MergeSelectionErrorKey);
        details.EditText(4, 1, "z");
        Assert.True(details.MergeSelectedGroups(), details.Error);
        var merged = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("a😀bz", merged.Text);
        Assert.Equal(2, merged.Karaoke.Length);
        Assert.Equal(ids[0], merged.Karaoke[0].Id);
        Assert.Equal(4, merged.Karaoke[0].Utf16Length);
        Assert.Equal(new MediaTime(1, 3), merged.Karaoke[0].Start);
        Assert.Equal(new MediaTime(4, 3), merged.Karaoke[0].End);
        Assert.Equal(line.Karaoke[3], merged.Karaoke[1]);
        Assert.Equal(5, KaraokeVisualStyleResolver.RangeStyleAt(merged, 1, KaraokeVisualState.ACTIVE)!.StrokeWidth);
        Assert.Equal(new SceneColor(1, 0, 0), KaraokeVisualStyleResolver.RangeStyleAt(merged, 1, KaraokeVisualState.ACTIVE)!.Fill);
        Assert.Equal(new SceneColor(0, 1, 0), KaraokeVisualStyleResolver.RangeStyleAt(merged, 3, KaraokeVisualState.ACTIVE)!.Fill);
        Assert.Equal(ids[0], Assert.Single(details.SelectedClipIds));
        Assert.Equal(ids[0], details.SelectedClipId);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(0, "Workbench.MergeKaraokeSelectionContiguous")]
    [InlineData(1, "Workbench.MergeKaraokeSelectionTimeAdjacent")]
    [InlineData(2, "Workbench.MergeKaraokeSelectionMode")]
    public async Task InvalidMultiMergeExplainsTheConstraintAndLeavesPendingTextAndUndoIntact(int scenario, string reason)
    {
        var document = Document();
        var line = document.Subtitles[0];
        if (scenario == 1)
        {
            line = line with { Karaoke = line.Karaoke.SetItem(1, line.Karaoke[1] with { Start = new(3, 4) }) };
        }
        else if (scenario == 2)
        {
            line = line with { Karaoke = line.Karaoke.SetItem(1, line.Karaoke[1] with { HighlightKind = KaraokeHighlightKind.STEP }) };
        }
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        var ids = scenario == 0 ? new[] { line.Karaoke[0].Id, line.Karaoke[2].Id }
            : [line.Karaoke[0].Id, line.Karaoke[1].Id];
        Assert.True(details.SelectClips(ids, ids[0]));
        Assert.Equal(reason, details.MergeSelectionErrorKey);
        details.EditText(4, 1, "z");
        Assert.False(details.MergeSelectedGroups());
        Assert.Equal(Localization.Get(reason), details.Error);
        Assert.Equal("a😀bz", details.Line!.Text);
        Assert.True(details.HasDrafts);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SelectionRejectsMissingIdsAndFollowsTextWithoutWritingTheDocument()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var line = document.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        var ids = line.Karaoke.Take(3).Select(clip => clip.Id).ToArray();
        Assert.True(details.SelectClips(ids, ids[1]));
        Assert.False(details.SelectClips([Guid.NewGuid()], null));
        Assert.Equal(ids, details.SelectedClipIds);
        Assert.True(details.FollowTextSelection(1, 2));
        Assert.Equal(ids[1], Assert.Single(details.SelectedClipIds));
        Assert.Equal("Workbench.MergeKaraokeSelectionMultiple", details.MergeSelectionErrorKey);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "a😀bc", End = new(4),
            Karaoke =
            [
                new(0, 1, new(1, 3), new(2, 3), SceneColor.White),
                new(1, 2, new(2, 3), new(1), new(1, 0, 0)),
                new(3, 1, new(1), new(4, 3), new(0, 1, 0)),
                new(4, 1, new(2), new(3), SceneColor.White)
            ],
            KaraokeStyleSpans = [new(1, 2, new() { StrokeWidth = 5 })]
        };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
