using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsWorkflowTests
{
    [Fact]
    public async Task ListTypeUsesExplicitOverridesAndKaraokePrecedenceWhileKeepingReadableText()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot.Subtitles[0];
        context.Editor.UpdateSubtitle(original.Id, line => line with { Karaoke = [], InlineSpans = [new(0, 1, new() { Bold = false })] });
        var row = context.Session.ViewModel.Subtitles.Rows.Single(value => value.Id == original.Id);
        Assert.Equal(Localization.Get("Workbench.SubtitleType.RICH_TEXT"), row.ContentType);
        Assert.Equal("ab", row.Text);
        context.Editor.UpdateSubtitle(original.Id, line => line with { Karaoke = original.Karaoke });
        Assert.Equal(Localization.Get("Workbench.SubtitleType.KARAOKE"), row.ContentType);
        Assert.Equal("ab", row.Text);
    }

    [Fact]
    public async Task ContinuousTextDraftCommitsOnceAndPreservesKaraokeIdentityAndTimes()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        details.EditText(0, 1, "甲");
        details.EditText(1, 1, "乙");
        Assert.Equal("甲乙", details.Line!.Text);
        Assert.Equal("ab", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.TryCommitDrafts());
        var edited = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("甲乙", edited.Text);
        Assert.Equal(original.Subtitles[0].Karaoke, edited.Karaoke);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidOtherDraftPreventsAnyPartialDetailsCommit()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        context.Session.Details.EditText(0, 1, "甲");
        context.Session.ViewModel.Subtitles.Rows[1].EndText = "invalid";
        Assert.False(context.Session.TryCommitDrafts());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("甲b", context.Session.Details.Line!.Text);
        context.Session.ViewModel.Subtitles.Rows[1].Accept(original.Subtitles[1]);
        Assert.True(context.Session.TryCommitDrafts());
        Assert.Equal("甲b", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task DurationRetainsInvalidDraftAndChangesOnlyTheSelectedEnd()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        details.SelectClip(original.Subtitles[0].Karaoke[0].Id);
        details.EditDuration("invalid");
        Assert.False(details.TryCommit());
        Assert.Equal("invalid", details.DurationText);
        Assert.Same(original, context.Editor.Snapshot);
        details.EditDuration("1.5");
        Assert.True(details.TryCommit());
        var edited = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(3, 2), edited.Karaoke[0].End);
        Assert.Equal(new MediaTime(1), edited.Karaoke[1].Start);
        Assert.Equal(new MediaTime(2), edited.Karaoke[1].End);
        Assert.Equal(original.Subtitles[0].End, edited.End);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task SelectedStyleAndLeadingGapRetainPreciseTimesAndNoOpRetainsRedo()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        details.ApplySelectionStyle(0, 1, new() { Bold = true });
        Assert.Equal(original.Subtitles[0].Karaoke, context.Editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Single(context.Editor.Snapshot.Subtitles[0].InlineSpans);
        details.EditLeadingDelay("0.333333333333");
        Assert.True(details.TryCommit());
        Assert.Equal(new MediaTime(333333333333, 1000000000000), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        Assert.True(context.Editor.Undo());
        Assert.True(context.Editor.CanRedo);
        Assert.True(details.TryCommit());
        Assert.True(context.Editor.CanRedo);
    }

    [Fact]
    public async Task RestoringOneStyleFieldKeepsOtherDraftsAndDoesNotCreateProjectTransaction()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.StyleDraft.FontFamily = "  ";
        details.StyleDraft.StrokeWidthText = "3.5";
        var fill = new SceneColor(2.5123456789012345, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234);
        details.StyleDraft.Fill.SetValue(fill);
        details.EditText(1, 1, "字");
        Assert.False(details.TryCommit());
        Assert.NotNull(details.Error);
        Assert.Equal("Selection.FontFamily", details.InvalidFieldKey);
        Assert.Same(original, context.Editor.Snapshot);

        details.RestoreStyleField(nameof(SubtitleDetailsStyleDraft.FontFamily), false);
        Assert.Null(details.Error);
        Assert.Null(details.InvalidFieldKey);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Equal("a字", details.Line!.Text);
        Assert.True(details.StyleDraft.IsDirty);
        Assert.Equal("3.5", details.StyleDraft.StrokeWidthText);
        Assert.True(details.StyleDraft.Fill.TryCommit(out var pending));
        Assert.Equal(fill, pending);

        Assert.True(details.TryCommit(), details.Error);
        var edited = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("a字", edited.Text);
        var selectedStyle = Assert.Single(edited.InlineSpans).Style.ApplyTo(edited.Style);
        Assert.Equal(3.5, selectedStyle.StrokeWidth);
        Assert.Equal(fill, selectedStyle.Fill);
        Assert.Equal(line.Style.FontFamily, selectedStyle.FontFamily);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData("Selection.FontSizeText")]
    [InlineData("Selection.Fill.Red")]
    [InlineData("Highlight.ShadowBlurText")]
    [InlineData("Start")]
    [InlineData("End")]
    [InlineData("Duration")]
    [InlineData("LeadingDelay")]
    [InlineData("Text")]
    public async Task DetailsFailurePublishesExactOriginToWorkspaceEvenWithASelectedClip(string field)
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        Assert.True(details.SelectClip(line.Karaoke[0].Id));
        switch (field)
        {
            case "Selection.FontSizeText":
                details.StyleDraft.FontSizeText = "7e-";
                break;
            case "Selection.Fill.Red":
                details.StyleDraft.Fill.Red.RawText = "7e-";
                break;
            case "Highlight.ShadowBlurText":
                Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
                details.HighlightDraft.ShadowBlurText = "7e-";
                break;
            case "Start":
                details.EditStart("7e-");
                break;
            case "End":
                details.EditEnd("7e-");
                break;
            case "Duration":
                details.EditDuration("7e-");
                break;
            case "LeadingDelay":
                details.EditLeadingDelay("7e-");
                break;
            case "Text":
                details.EditText(1, 0, "\ud800");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal(field, details.InvalidFieldKey);
        Assert.Equal("subtitleDetails", context.Session.ViewModel.InvalidPanelId);
        Assert.Equal(field, context.Session.ViewModel.InvalidFieldKey);
        Assert.NotNull(details.Error);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        details.Restore("All");
        Assert.Null(details.InvalidFieldKey);
        Assert.Null(details.Error);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Null(context.Session.ViewModel.InvalidFieldKey);
        Assert.Null(context.Session.ViewModel.InvalidPanelId);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine { Text = "ab", End = new(4), Karaoke =
            [new(0, 1, MediaTime.Zero, new(1), SceneColor.White), new(1, 1, new(1), new(2), SceneColor.White)] };
        var second = new SubtitleLine { Text = "second", Start = new(5), End = new(7) };
        return new() { Subtitles = [first, second], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End },
             new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End }] };
    }
}
