using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsHighlightSelectionTests
{
    [Fact]
    public async Task HighlightDraftPreviewsWithoutCommittingAndKeepsSelectionFrozenOnInvalidInput()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.HighlightDraft.StrokeWidthText = "7.25";
        Assert.Equal(7.25, ActiveStyleAt(details.Line!, 0)!.StrokeWidth);
        Assert.Null(ActiveStyleAt(details.Line!, 1));
        Assert.Same(original, context.Editor.Snapshot);
        details.HighlightDraft.StrokeWidthText = "-";
        Assert.Equal(7.25, ActiveStyleAt(details.Line!, 0)!.StrokeWidth);
        Assert.False(details.SetStyleSelection(1, 1));
        Assert.Equal("-", details.HighlightDraft.StrokeWidthText);
        Assert.True(details.CompleteInput(nameof(SubtitleKaraokeStyleDraft.StrokeWidthText), true));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(details.SetStyleSelection(1, 1));
    }

    [Fact]
    public async Task WholeSentenceDefaultsRetainOverridesWhileSelectedPresetsOnlyChangeExistingCharacters()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var line = context.Editor.Snapshot.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        var preset = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Red", new() { Fill = new(1, 0, 0), StrokeWidth = 3 });
        Assert.True(details.SetStyleSelection(0, 1));
        details.ApplyHighlightStyle(preset);
        var selected = context.Editor.Snapshot.Subtitles[0];
        Assert.Null(selected.KaraokeStyle);
        Assert.Equal(new SceneColor(1, 0, 0), ActiveStyleAt(selected, 0)!.Fill);
        Assert.Same(line.Karaoke[1], selected.Karaoke[1]);
        Assert.True(details.SetStyleSelection(0, 0));
        details.ApplyHighlightStyle(preset with { Fill = new(0, 1, 0) });
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new SceneColor(0, 1, 0), changed.KaraokeStyle!.Fill);
        Assert.Equal(selected.Karaoke, changed.Karaoke);
        Assert.True(details.SetStyleSelection(0, 1));
        details.ApplyHighlightStyle(null);
        Assert.Null(ActiveStyleAt(context.Editor.Snapshot.Subtitles[0], 0));
    }

    [Fact]
    public async Task CompletingOneValidInputCommitsOnlyThatFieldAndPreservesAnotherInvalidRawDraft()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.StyleDraft.FontSizeText = "88";
        details.StyleDraft.ShadowBlurText = "invalid";
        Assert.True(details.CompleteInput(nameof(SubtitleDetailsStyleDraft.FontSizeText), false));
        Assert.Equal(88, Assert.Single(context.Editor.Snapshot.Subtitles[0].InlineSpans).Style.FontSize);
        Assert.Equal("invalid", details.StyleDraft.ShadowBlurText);
        Assert.True(details.StyleDraft.IsDirty);
        Assert.False(details.TryCommit());
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task RestoringOneInvalidInputKeepsTheOtherValidDraftUncommitted()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        var originalBlur = details.HighlightDraft.ShadowBlurText;
        details.HighlightDraft.StrokeWidthText = "3.25";
        details.HighlightDraft.ShadowBlurText = "invalid";
        Assert.True(details.CompleteInput(nameof(SubtitleKaraokeStyleDraft.ShadowBlurText), true));
        Assert.Equal("3.25", details.HighlightDraft.StrokeWidthText);
        Assert.Equal(originalBlur, details.HighlightDraft.ShadowBlurText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(details.TryCommit());
        Assert.Equal(3.25, ActiveStyleAt(context.Editor.Snapshot.Subtitles[0], 0)!.StrokeWidth);
    }

    [Fact]
    public async Task CompletingTextAndDurationPreservesUnrelatedInvalidStyleDrafts()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.StyleDraft.ShadowBlurText = "invalid";
        details.EditText(1, 1, "C");
        Assert.True(details.CompleteInput("Text", false));
        Assert.Equal("AC", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal("invalid", details.StyleDraft.ShadowBlurText);
        Assert.Equal(original.Subtitles[0].InlineSpans, context.Editor.Snapshot.Subtitles[0].InlineSpans);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        details.RestoreStyleField(nameof(SubtitleDetailsStyleDraft.ShadowBlurText), false);
        Assert.True(details.SelectClip(original.Subtitles[0].Karaoke[0].Id));
        details.StyleDraft.ShadowBlurText = "invalid";
        details.EditDuration("3.5");
        Assert.True(details.CompleteInput("Duration", false));
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(7, 2), context.Editor.Snapshot.Subtitles[0].Karaoke[0].End);
        Assert.Equal("invalid", details.StyleDraft.ShadowBlurText);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task CompletingInvalidCodeRestoresCodeWithoutAcceptingOrDiscardingOtherStyleDrafts()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        var originalSource = details.Source;
        details.StyleDraft.StrokeWidthText = "3.25";
        details.EditSource("{\\unsupported}AB");
        Assert.NotNull(details.Error);
        Assert.True(details.CompleteInput("Code", false));
        Assert.Equal(originalSource, details.Source);
        Assert.Equal("3.25", details.StyleDraft.StrokeWidthText);
        Assert.True(details.StyleDraft.IsDirty);
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task CompletingOneStyleFieldKeepsPendingTextAndDurationOutsideTheTransaction()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(original.Subtitles[0].Karaoke[0].Id));
        Assert.True(details.SetStyleSelection(0, 1));
        details.EditText(1, 1, "C");
        details.EditDuration("invalid");
        details.StyleDraft.FontSizeText = "88";
        Assert.True(details.CompleteInput(nameof(SubtitleDetailsStyleDraft.FontSizeText), false));
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("AB", changed.Text);
        Assert.Equal(88, Assert.Single(changed.InlineSpans).Style.FontSize);
        Assert.Equal(original.Subtitles[0].Karaoke, changed.Karaoke);
        Assert.Equal("AC", details.Line!.Text);
        Assert.Equal("invalid", details.DurationText);
        details.HighlightDraft.StrokeWidthText = "7";
        Assert.True(details.CompleteInput(nameof(SubtitleKaraokeStyleDraft.StrokeWidthText), true));
        Assert.Equal("AB", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal(7, ActiveStyleAt(context.Editor.Snapshot.Subtitles[0], 0)!.StrokeWidth);
        Assert.Null(ActiveStyleAt(context.Editor.Snapshot.Subtitles[0], 1));
        Assert.Equal("AC", details.Line!.Text);
        Assert.True(details.CompleteInput("Text", false));
        Assert.Equal("AC", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.Equal(88, Assert.Single(context.Editor.Snapshot.Subtitles[0].InlineSpans).Style.FontSize);
        Assert.Equal("invalid", details.DurationText);
        Assert.True(details.CompleteInput("Duration", false));
        Assert.Equal("1", details.DurationText);
        Assert.True(context.Editor.Undo());
        Assert.Equal("AB", context.Editor.Snapshot.Subtitles[0].Text);
        Assert.True(context.Editor.Undo());
        Assert.Null(ActiveStyleAt(context.Editor.Snapshot.Subtitles[0], 0));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task CompletingDurationKeepsPendingTextAndStylesOutsideTheTransaction()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(original.Subtitles[0].Karaoke[0].Id));
        Assert.True(details.SetStyleSelection(0, 1));
        details.EditText(1, 1, "C");
        details.StyleDraft.StrokeWidthText = "3.25";
        details.EditDuration("3.5");
        Assert.True(details.CompleteInput("Duration", false));
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("AB", changed.Text);
        Assert.Equal(original.Subtitles[0].InlineSpans, changed.InlineSpans);
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(7, 2), changed.Karaoke[0].End);
        Assert.Equal("AC", details.Line!.Text);
        Assert.Equal("3.25", details.StyleDraft.StrokeWidthText);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task HighlightReadsTheEffectiveVisualAndStylesUntimedSelectionsWithoutCreatingTiming()
    {
        var document = Document();
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Orange", new()
        {
            Fill = new(1, 0.5, 0), StrokeWidth = 4, ShadowOffset = new(20, 30)
        });
        var line = document.Subtitles[0] with
        {
            Text = "ABX", KaraokeStyle = highlight,
            InlineSpans = [new(0, 1, new() { FontSize = 60, Fill = new(0, 1, 0), ShadowOffset = new(-7, 8) }),
                new(1, 1, new() { FontSize = 12, ShadowOffset = new(10, 11) })],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 9 })]
        };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        Assert.True(details.SelectionHasTimedKaraoke);
        Assert.Equal(60, details.SelectionStyle().FontSize);
        Assert.Equal(highlight.Fill, details.HighlightStyle().Fill);
        Assert.Equal(9, details.HighlightStyle().StrokeWidth);
        Assert.Equal(highlight.ShadowOffset, details.HighlightStyle().ShadowOffset);
        details.HighlightDraft.ShadowXText = "40";
        Assert.True(details.TryCommit());
        Assert.Equal(new ScenePoint(40, 30), ActiveStyleAt(context.Editor.Snapshot.Subtitles[0], 0)!.ShadowOffset);
        Assert.Equal(line.InlineSpans, context.Editor.Snapshot.Subtitles[0].InlineSpans);
        Assert.True(details.SetStyleSelection(2, 1));
        Assert.False(details.SelectionHasTimedKaraoke);
        var applied = context.Editor.Snapshot;
        details.ApplyHighlightStyle(highlight);
        var untimed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(highlight.Fill, ActiveStyleAt(untimed, 2)!.Fill);
        Assert.Equal(applied.Subtitles[0].Karaoke, untimed.Karaoke);
        Assert.Equal(applied.Subtitles[0].InlineSpans, untimed.InlineSpans);
        Assert.True(details.SetStyleSelection(0, 0));
        Assert.True(details.SelectionHasTimedKaraoke);
        Assert.Equal(highlight, details.HighlightStyle());
    }

    [Fact]
    public async Task ARemovedDraftSelectionDoesNotThrowDuringPreviewOrCommitAnotherFieldsRawDraft()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(1, 1));
        details.StyleDraft.FontSizeText = "88";
        details.StyleDraft.StrokeWidthText = "invalid";
        details.EditText(0, 2, string.Empty);
        Assert.Equal(string.Empty, details.Line!.Text);
        Assert.True(details.CompleteInput(nameof(SubtitleDetailsStyleDraft.FontSizeText), false));
        Assert.Equal("invalid", details.StyleDraft.StrokeWidthText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(details.StyleDraft.IsFieldDirty(nameof(SubtitleDetailsStyleDraft.FontSizeText)));
    }

    [Fact]
    public async Task InvalidHighlightDraftKeepsTheSubtitleAndCharacterTargetsUntilTheFieldIsRestored()
    {
        var document = Document();
        var first = document.Subtitles[0];
        var second = new SubtitleLine { Text = "Second", Start = new(5), End = new(9) };
        document = document with
        {
            Subtitles = [first, second], Layers = [document.Layers[0], new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End
            }]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(first.Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.HighlightDraft.StrokeWidthText = "invalid";
        Assert.False(details.SetStyleSelection(1, 1));
        context.Session.SelectCue(second.Id);
        Assert.Equal(first.Id, context.Session.SelectedCueId);
        Assert.Equal("invalid", details.HighlightDraft.StrokeWidthText);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.True(details.CompleteInput(nameof(SubtitleKaraokeStyleDraft.StrokeWidthText), true));
        Assert.True(details.SetStyleSelection(1, 1));
        context.Session.SelectCue(second.Id);
        Assert.Equal(second.Id, context.Session.SelectedCueId);
        Assert.Same(document, context.Editor.Snapshot);
    }

    private static KaraokeVisualStyleOverride? ActiveStyleAt(SubtitleLine line, int utf16Offset)
    {
        return KaraokeVisualStyleResolver.StyleAt(line.KaraokeStyleSpans, utf16Offset, KaraokeVisualState.ACTIVE);
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine { Text = "AB", End = new(4), Karaoke =
            [new(0, 1, new(0), new(1), SceneColor.White), new(1, 1, new(1), new(2), SceneColor.White)] };
        return new() { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
