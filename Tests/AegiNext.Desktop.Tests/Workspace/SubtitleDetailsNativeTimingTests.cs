using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsNativeTimingTests
{
    [Fact]
    public async Task FirstEnableGeneratesCompleteGraphemesAndLaterEnablesPreserveWholeGroups()
    {
        var document = Document();
        var line = document.Subtitles[0] with { Text = "e\u0301😀z", Karaoke = [] };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        details.SetKaraokeEnabled(true);
        var generated = context.Editor.Snapshot;
        Assert.Equal([2, 2, 1], generated.Subtitles[0].Karaoke.Select(clip => clip.Utf16Length));
        Assert.Equal(new MediaTime(4, 3), generated.Subtitles[0].Karaoke[0].End);
        Assert.Equal(new MediaTime(4), generated.Subtitles[0].Karaoke[^1].End);
        details.SetKaraokeEnabled(true);
        Assert.Same(generated, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        Assert.True(details.CreateTiming(0, 5, "0.25", "3.5"), details.Error);
        var grouped = context.Editor.Snapshot;
        var group = Assert.Single(grouped.Subtitles[0].Karaoke);
        details.SetKaraokeEnabled(true);
        Assert.Same(grouped, context.Editor.Snapshot);
        details.SetKaraokeEnabled(false);
        details.SetKaraokeEnabled(true);
        Assert.Same(group, Assert.Single(context.Editor.Snapshot.Subtitles[0].Karaoke));
    }

    [Fact]
    public async Task EditingOneEndpointPreservesTheOtherExactRationalAndDoesNotMoveNeighbors()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var line = context.Editor.Snapshot.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(line.Karaoke[0].Id));
        details.EditStart(details.StartText);
        details.EditEnd("1.5");
        Assert.True(details.TryCommit(), details.Error);
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(1, 3), changed.Karaoke[0].Start);
        Assert.Equal(new MediaTime(3, 2), changed.Karaoke[0].End);
        Assert.Same(line.Karaoke[1], changed.Karaoke[1]);
    }

    [Fact]
    public async Task ARangeGestureCommitsPendingTextAndExactRangeInOneUndoAndRejectsStaleSources()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        details.EditText(1, 1, "C");
        var baseline = details.Line!;
        Assert.True(details.SetRange(baseline, MediaTime.Zero, line.Karaoke[0].Id, new(2, 3), new(5, 3)), details.Error);
        Assert.Equal("AC", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal(new MediaTime(2, 3), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        Assert.False(details.SetRange(baseline, MediaTime.Zero, line.Karaoke[0].Id, new(1), new(2)));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task VisualStatesStyleUntimedAndDisabledTextWithoutChangingTimingAndKeepAuthoredKoStroke()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Karaoke = [],
            InactiveKaraoke = [document.Subtitles[0].Karaoke[0] with { HighlightKind = KaraokeHighlightKind.OUTLINE_STEP }]
        };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.INACTIVE));
        details.HighlightDraft.StrokeWidthText = "6.5";
        Assert.True(details.TryCommit(), details.Error);
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Empty(changed.Karaoke);
        Assert.Equal(line.InactiveKaraoke, changed.InactiveKaraoke);
        Assert.Equal(6.5, KaraokeVisualStyleResolver.RangeStyleAt(changed, 1, KaraokeVisualState.INACTIVE)!.StrokeWidth);
        Assert.Equal(6.5, details.HighlightStyle().StrokeWidth);
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        details.HighlightDraft.Fill.SetValue(new(1, 0, 0));
        Assert.True(details.TryCommit(), details.Error);
        Assert.Equal(new SceneColor(1, 0, 0), KaraokeVisualStyleResolver.RangeStyleAt(context.Editor.Snapshot.Subtitles[0], 1, KaraokeVisualState.ACTIVE)!.Fill);
    }

    [Fact]
    public async Task ManualCreationRequiresExplicitSignedTimesAndSplittingRequiresAnExplicitCommand()
    {
        var document = Document();
        var line = document.Subtitles[0] with { Karaoke = [] };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.False(details.CreateTiming(0, 2, "", ""));
        details.DismissCreationError();
        Assert.True(details.CreateTiming(0, 2, "0.333333333333", "2"), details.Error);
        Assert.Single(context.Editor.Snapshot.Subtitles[0].Karaoke);
        Assert.True(details.SplitSelectedGroup(), details.Error);
        Assert.Equal(2, context.Editor.Snapshot.Subtitles[0].Karaoke.Length);
        var ids = context.Editor.Snapshot.Subtitles[0].Karaoke.Select(clip => clip.Id).ToArray();
        Assert.True(details.SelectClips(ids, ids[0]));
        Assert.True(details.MergeSelectedGroups(), details.Error);
        Assert.Single(context.Editor.Snapshot.Subtitles[0].Karaoke);
    }

    [Fact]
    public async Task BothEndpointDraftsAreValidatedTogetherAndFieldCompletionKeepsOtherRawDrafts()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(line.Karaoke[0].Id));
        details.EditStart("4");
        details.EditEnd("5");
        Assert.True(details.TryCommit(), details.Error);
        Assert.Equal(new MediaTime(4), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        Assert.Equal(new MediaTime(5), context.Editor.Snapshot.Subtitles[0].Karaoke[0].End);
        Assert.True(context.Editor.Undo());
        details.EditText(1, 1, "C");
        details.EditStart("invalid");
        details.EditEnd("1.5");
        details.StyleDraft.StrokeWidthText = "invalid";
        Assert.True(details.CompleteInput("End", false));
        Assert.Equal("AB", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal(new MediaTime(1, 3), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        Assert.Equal("AC", details.Line!.Text);
        Assert.Equal("invalid", details.StartText);
        Assert.Equal("invalid", details.StyleDraft.StrokeWidthText);
        Assert.True(details.CompleteInput("Start", false));
        Assert.Equal("0.3333333333333333", details.StartText);
        Assert.Equal("invalid", details.StyleDraft.StrokeWidthText);
    }

    [Fact]
    public async Task SignedVisibleOffsetsAndEarliestDelayPreserveContentClockAndFrozenOffset()
    {
        var document = Document();
        document = document with
        {
            Subtitles = [document.Subtitles[0] with { Start = new(5), End = new(9) }],
            Layers = [document.Layers[0] with { Start = new(5), End = new(9), AnimationOffset = new(2) }]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var line = document.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(line.Karaoke[0].Id));
        Assert.Equal("-1.6666666666666667", details.StartText);
        details.EditStart("-1.5");
        details.EditEnd("-0.5");
        Assert.True(details.TryCommit(), details.Error);
        Assert.Equal(new MediaTime(1, 2), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        Assert.Equal(new MediaTime(3, 2), context.Editor.Snapshot.Subtitles[0].Karaoke[0].End);
        Assert.True(details.SetLeadingDelay(new(-1)), details.Error);
        Assert.Equal(new MediaTime(1), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        var baseline = details.Line!;
        context.Editor.Apply("Change offset", snapshot => snapshot with { Layers = [snapshot.Layers[0] with { AnimationOffset = new(3) }] });
        Assert.False(details.SetRange(baseline, new(2), line.Karaoke[0].Id, new(1), new(2)));
    }

    [Fact]
    public async Task DurationRippleRequiresTheExplicitSwitchAndPreservesPendingTextInOneUndo()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        details.EditText(1, 1, "C");
        Assert.True(details.SetDuration(line.Karaoke[0].Id, new(2)), details.Error);
        Assert.Same(line.Karaoke[1], context.Editor.Snapshot.Subtitles[0].Karaoke[1]);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        details.LinkedTimingEnabled = true;
        Assert.True(details.SetDuration(line.Karaoke[0].Id, new(2)), details.Error);
        Assert.Equal(new MediaTime(10, 3), context.Editor.Snapshot.Subtitles[0].Karaoke[1].Start);
        Assert.Equal(new MediaTime(13, 3), context.Editor.Snapshot.Subtitles[0].Karaoke[1].End);
    }

    [Fact]
    public async Task SwitchingVisualStateCommitsOnlyThePreviousStateAndClearPreservesTheOtherState()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var line = context.Editor.Snapshot.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        Assert.True(details.SetStyleSelection(0, 1));
        details.HighlightDraft.StrokeWidthText = "7";
        Assert.True(details.SetVisualState(KaraokeVisualState.INACTIVE));
        Assert.Equal(7, KaraokeVisualStyleResolver.RangeStyleAt(context.Editor.Snapshot.Subtitles[0], 0, KaraokeVisualState.ACTIVE)!.StrokeWidth);
        details.HighlightDraft.StrokeWidthText = "invalid";
        Assert.False(details.SetVisualState(null));
        Assert.Equal(KaraokeVisualState.INACTIVE, details.VisualState);
        Assert.True(details.CompleteInput("StrokeWidthText", true));
        details.HighlightDraft.StrokeWidthText = "3";
        Assert.True(details.TryCommit(), details.Error);
        details.ApplyHighlightStyle(null);
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Null(KaraokeVisualStyleResolver.RangeStyleAt(changed, 0, KaraokeVisualState.INACTIVE));
        Assert.Equal(7, KaraokeVisualStyleResolver.RangeStyleAt(changed, 0, KaraokeVisualState.ACTIVE)!.StrokeWidth);
        Assert.True(details.SetVisualState(null));
        Assert.Null(details.VisualState);
    }

    [Fact]
    public async Task CancellingTimingOrCreationLeavesNativeTextAndVisualDraftsIntact()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        Assert.True(details.SelectClip(line.Karaoke[0].Id));
        Assert.True(details.SetStyleSelection(0, 1));
        details.EditText(1, 1, "C");
        details.HighlightDraft.StrokeWidthText = "3.5";
        details.EditStart("invalid");
        details.EditEnd("2");
        details.EditDuration("4");
        details.EditLeadingDelay("invalid");
        details.Restore("Timing");
        Assert.Equal("0.3333333333333333", details.StartText);
        Assert.Equal("1", details.EndText);
        Assert.Equal("0.6666666666666667", details.DurationText);
        Assert.Equal("0.3333333333333333", details.LeadingDelayText);
        Assert.Equal("AC", details.Line!.Text);
        Assert.Equal("3.5", details.HighlightDraft.StrokeWidthText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(details.CreateTiming(0, 1, "invalid", "2"));
        Assert.Equal("Start", details.InvalidFieldKey);
        details.DismissCreationError();
        Assert.Equal("AC", details.Line!.Text);
        Assert.Equal("3.5", details.HighlightDraft.StrokeWidthText);
        Assert.True(details.TryCommit(), details.Error);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task LeadingDelayUsesTheEarliestTimeWhenTextOrderDiffersFromTimeOrder()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Karaoke = [document.Subtitles[0].Karaoke[0] with { Start = new(3), End = new(4) },
                document.Subtitles[0].Karaoke[1] with { Start = new(1), End = new(2) }]
        };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.Equal("1", details.LeadingDelayText);
        details.EditLeadingDelay("0.5");
        Assert.True(details.CompleteInput("LeadingDelay", false));
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(5, 2), changed.Karaoke[0].Start);
        Assert.Equal(new MediaTime(1, 2), changed.Karaoke[1].Start);
        Assert.Equal(line.Start, changed.Start);
        Assert.Equal(line.End, changed.End);
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "AB", End = new(4),
            Karaoke = [new(0, 1, new(1, 3), new(1), SceneColor.White), new(1, 1, new(2), new(3), SceneColor.White)]
        };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
