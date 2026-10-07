using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsAlignmentTests
{
    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public async Task AlignmentWithoutTextSelectionPreservesPlacementAndContentAndCommitsOnce(SubtitleTextAlignment alignment)
    {
        var document = Document();
        var line = document.Subtitles[0];
        document = document with
        {
            Subtitles = [line with
            {
                Style = line.Style with
                {
                    TextAlign = alignment == SubtitleTextAlignment.LEFT ? SubtitleTextAlignment.RIGHT : SubtitleTextAlignment.LEFT
                }
            }, document.Subtitles[1]]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var original = context.Editor.Snapshot;

        Assert.True(context.Session.Details.ApplyTextAlignment(alignment));
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(original.Subtitles[0] with { Style = original.Subtitles[0].Style with { TextAlign = alignment } }, changed);
        Assert.Equal(document.Layers, context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Equal(alignment, context.Session.Details.Line!.Style.TextAlign);
    }

    [Fact]
    public async Task AlignmentAndValidDetailsDraftsShareOneTransaction()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        Assert.True(details.SetStyleSelection(0, 1));
        details.EditText(1, 1, "Z");
        details.StyleDraft.FontSizeText = "88";

        Assert.True(details.ApplyTextAlignment(SubtitleTextAlignment.LEFT));
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal("AZCD\nEF", changed.Text);
        Assert.Equal(88, changed.InlineSpans[0].Style.FontSize);
        Assert.Equal(SubtitleTextAlignment.LEFT, changed.Style.TextAlign);
        Assert.Equal(original.Subtitles[0].Karaoke, changed.Karaoke);
        Assert.False(details.StyleDraft.IsDirty);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidStyleOrDurationPreventsPartialAlignmentCommit(bool duration)
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(line.Karaoke[0].Id));
        Assert.True(details.SetStyleSelection(0, 1));
        details.EditText(1, 1, "Z");
        if (duration)
        {
            details.EditDuration("invalid");
        }
        else
        {
            details.StyleDraft.FontSizeText = "invalid";
        }

        Assert.False(details.ApplyTextAlignment(SubtitleTextAlignment.LEFT));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("AZCD\nEF", details.Line!.Text);
        Assert.Null(details.Line.Style.TextAlign);
        Assert.Equal("invalid", duration ? details.DurationText : details.StyleDraft.FontSizeText);
        Assert.Equal(duration ? "Duration" : "Selection.FontSizeText", details.InvalidFieldKey);
    }

    [Fact]
    public async Task ClickingTheEffectiveModeKeepsInheritedAlignmentAndRedo()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        context.Session.SelectCue(context.Editor.Snapshot.Subtitles[0].Id);
        var original = context.Editor.Snapshot;
        var details = context.Session.Details;

        Assert.True(details.ApplyTextAlignment(SubtitleTextAlignment.CENTER));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.Null(details.Line!.Style.TextAlign);
        Assert.False(context.Editor.CanUndo);
        Assert.True(details.ApplyTextAlignment(SubtitleTextAlignment.LEFT));
        var changed = context.Editor.Snapshot;
        Assert.True(details.ApplyTextAlignment(SubtitleTextAlignment.LEFT));
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.True(details.ApplyTextAlignment(SubtitleTextAlignment.CENTER));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Editor.CanRedo);
    }

    [Fact]
    public async Task AlignmentRequiresATargetAndRejectsUnknownValues()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        Assert.False(context.Session.Details.ApplyTextAlignment(SubtitleTextAlignment.LEFT));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Session.Details.ApplyTextAlignment((SubtitleTextAlignment)99));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument Document()
    {
        var first = new SubtitleLine
        {
            Text = "ABCD\nEF", End = new(4),
            Style = new()
            {
                Alignment = TextAlignment.BOTTOM_CENTER,
                Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 1), Offset = new(7, -12) }
            },
            InlineSpans = [new(0, 1, new() { Bold = true })],
            Karaoke = [new(0, 4, MediaTime.Zero, new(1), SceneColor.White), new(5, 2, new(1), new(2), SceneColor.White)]
        };
        var second = new SubtitleLine { Text = "second", Start = new(5), End = new(7) };
        return new()
        {
            Subtitles = [first, second],
            Layers =
            [
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End },
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End }
            ]
        };
    }
}
