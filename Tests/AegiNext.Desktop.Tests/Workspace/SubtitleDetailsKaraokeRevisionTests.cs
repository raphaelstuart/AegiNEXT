using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsKaraokeRevisionTests
{
    [Fact]
    public async Task OverflowAndLeadingDelayCommitWithoutBlockingFurtherEditsOrMovingCueBounds()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        details.SelectClip(line.Karaoke[0].Id);
        details.LinkedTimingEnabled = true;
        details.EditDuration("9");
        Assert.True(details.TryCommit(), details.Error);
        Assert.Equal(new MediaTime(10), context.Editor.Snapshot.Subtitles[0].Karaoke[^1].End);
        Assert.Equal(line.End, context.Editor.Snapshot.Subtitles[0].End);
        Assert.Null(details.Error);
        details.EditDuration("1.5");
        details.EditLeadingDelay("8");
        Assert.True(details.TryCommit(), details.Error);
        Assert.Equal(new MediaTime(8), context.Editor.Snapshot.Subtitles[0].Karaoke[0].Start);
        Assert.Equal(new MediaTime(21, 2), context.Editor.Snapshot.Subtitles[0].Karaoke[^1].End);
        details.EditLeadingDelay("0");
        Assert.True(details.TryCommit(), details.Error);
        Assert.Equal(new MediaTime(5, 2), context.Editor.Snapshot.Subtitles[0].Karaoke[^1].End);
        Assert.Equal(line.Start, details.Line!.Start);
        Assert.Equal(line.End, details.Line.End);
        Assert.True(context.Editor.Undo());
        Assert.True(context.Editor.Undo());
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task WholeHighlightEditingPreservesActiveOverridesInlineFontsTimesAndExactColors()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(0, 1, new() { FontFamily = "Noto Sans SC", FontSize = 27 })],
            KaraokeStyleSpans = [new(0, 1, new() { Fill = SceneColor.Black })]
        };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        var exactColor = new SceneColor(2.5123456789012345, 0.1234567890123456, 0.8, 0.7312345678901234);
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "HDR", new() { Fill = exactColor, StrokeWidth = 3 });
        details.ApplyHighlightStyle(highlight);
        details.HighlightDraft.StrokeWidthText = "4.5";
        Assert.True(details.TryCommit(), details.Error);
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(exactColor, changed.KaraokeStyle!.Fill);
        Assert.Equal(3, changed.KaraokeStyle.StrokeWidth);
        Assert.Equal(4.5, KaraokeVisualStyleResolver.RangeStyleAt(changed, 1, KaraokeVisualState.ACTIVE)!.StrokeWidth);
        Assert.Equal(line.Style, changed.Style);
        Assert.Equal(line.InlineSpans, changed.InlineSpans);
        Assert.Equal(line.Karaoke.Select(value => (value.Id, value.Start, value.End)),
            changed.Karaoke.Select(value => (value.Id, value.Start, value.End)));
        Assert.Equal(SceneColor.Black, KaraokeVisualStyleResolver.RangeStyleAt(changed, 0, KaraokeVisualState.ACTIVE)!.Fill);
        Assert.True(context.Editor.Undo());
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task ExplicitGenerationCreatesFullGraphemesAndDisablingRetainsHighlightAndRichContent()
    {
        var document = Document();
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "saved", new());
        var line = document.Subtitles[0] with
        {
            Text = "甲e\u0301👩‍💻", Karaoke = [], KaraokeStyle = highlight,
            InlineSpans = [new(0, 1, new() { Bold = true })]
        };
        document = document with { Subtitles = [line] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        Assert.True(details.GenerateAllTiming(), details.Error);
        var enabled = context.Editor.Snapshot.Subtitles[0];
        Assert.True(details.IsKaraokeEnabled);
        Assert.Equal(StringInfo.ParseCombiningCharacters(line.Text), enabled.Karaoke.Select(clip => clip.Utf16Start));
        Assert.Equal(3, enabled.Karaoke.Length);
        Assert.Equal(line.End - line.Start, enabled.Karaoke[^1].End);
        details.SetKaraokeEnabled(false);
        var disabled = context.Editor.Snapshot.Subtitles[0];
        Assert.False(details.IsKaraokeEnabled);
        Assert.Empty(disabled.Karaoke);
        Assert.Equal(enabled.Karaoke.ToArray(), disabled.InactiveKaraoke.ToArray());
        Assert.Equal(highlight, disabled.KaraokeStyle);
        Assert.Equal(line.InlineSpans, disabled.InlineSpans);
        Assert.Equal(line.Text, disabled.Text);
        Assert.True(context.Editor.Undo());
        Assert.Equal(enabled, context.Editor.Snapshot.Subtitles[0]);
    }

    [Fact]
    public async Task DisablingAndRestoringPreservesExactKaraokeDataAndOneTransactionPerToggle()
    {
        var document = DetailedKaraokeDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        Assert.True(details.SelectClip(line.Karaoke[1].Id));
        details.SetKaraokeEnabled(false);
        var disabled = context.Editor.Snapshot;
        var disabledLine = disabled.Subtitles[0];
        Assert.False(details.IsKaraokeEnabled);
        Assert.Empty(disabledLine.Karaoke);
        Assert.Equal(line.Karaoke.ToArray(), disabledLine.InactiveKaraoke.ToArray());
        Assert.Equal(line.KaraokeStyle, disabledLine.KaraokeStyle);
        Assert.Equal(line.KaraokeStyleSpans, disabledLine.KaraokeStyleSpans);
        Assert.Equal(line.Style, disabledLine.Style);
        Assert.Equal(line.InlineSpans, disabledLine.InlineSpans);
        Assert.Equal(line.Text, disabledLine.Text);
        Assert.Equal(SubtitleContentKind.RICH_TEXT, disabledLine.ContentKind);
        details.SetKaraokeEnabled(false);
        Assert.Same(disabled, context.Editor.Snapshot);
        details.SetKaraokeEnabled(true);
        var restored = context.Editor.Snapshot;
        Assert.True(details.IsKaraokeEnabled);
        Assert.Equal(line.Karaoke.ToArray(), restored.Subtitles[0].Karaoke.ToArray());
        Assert.Empty(restored.Subtitles[0].InactiveKaraoke);
        Assert.Equal(line.KaraokeStyle, restored.Subtitles[0].KaraokeStyle);
        Assert.Equal(line.KaraokeStyleSpans, restored.Subtitles[0].KaraokeStyleSpans);
        Assert.Equal(line.Style, restored.Subtitles[0].Style);
        Assert.Equal(line.InlineSpans, restored.Subtitles[0].InlineSpans);
        details.SetKaraokeEnabled(true);
        Assert.Same(restored, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(disabled, context.Editor.Snapshot);
        Assert.False(details.IsKaraokeEnabled);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(details.IsKaraokeEnabled);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(disabled, context.Editor.Snapshot);
        Assert.False(details.IsKaraokeEnabled);
        Assert.True(context.Editor.Redo());
        Assert.Same(restored, context.Editor.Snapshot);
        Assert.True(details.IsKaraokeEnabled);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task SavingDisabledKaraokeAndOpeningANewSessionRestoresEveryStoredParameter()
    {
        var document = DetailedKaraokeDocument();
        var expected = document.Subtitles[0];
        ProjectDocument loaded;
        await using (var context = new WorkspaceSessionTestContext(document))
        {
            await context.InitializeAsync();
            context.Session.SelectCue(expected.Id);
            context.Session.Details.SetKaraokeEnabled(false);
            var path = Path.Combine(context.DirectoryPath, "disabled-karaoke.aeginext");
            await ProjectStore.SaveAsync(context.Editor.Snapshot, path);
            loaded = await ProjectStore.LoadAsync(path);
            Assert.Empty(loaded.Subtitles[0].Karaoke);
            Assert.Equal(expected.Karaoke.ToArray(), loaded.Subtitles[0].InactiveKaraoke.ToArray());
            Assert.Equal(expected.KaraokeStyle, loaded.Subtitles[0].KaraokeStyle);
            Assert.Equal(expected.KaraokeStyleSpans.ToArray(), loaded.Subtitles[0].KaraokeStyleSpans.ToArray());
        }
        await using var reopened = new WorkspaceSessionTestContext(loaded);
        await reopened.InitializeAsync();
        reopened.Session.SelectCue(expected.Id);
        var disabled = reopened.Editor.Snapshot;
        Assert.False(reopened.Session.Details.IsKaraokeEnabled);
        Assert.False(reopened.Editor.CanUndo);
        reopened.Session.Details.SetKaraokeEnabled(true);
        var restored = reopened.Editor.Snapshot;
        Assert.True(reopened.Session.Details.IsKaraokeEnabled);
        Assert.Equal(expected.Karaoke.ToArray(), restored.Subtitles[0].Karaoke.ToArray());
        Assert.Empty(restored.Subtitles[0].InactiveKaraoke);
        Assert.Equal(expected.KaraokeStyle, restored.Subtitles[0].KaraokeStyle);
        Assert.Equal(expected.KaraokeStyleSpans.ToArray(), restored.Subtitles[0].KaraokeStyleSpans.ToArray());
        Assert.Equal(expected.InlineSpans.ToArray(), restored.Subtitles[0].InlineSpans.ToArray());
        Assert.Equal(expected.Style, restored.Subtitles[0].Style);
        Assert.True(reopened.Editor.Undo());
        Assert.Same(disabled, reopened.Editor.Snapshot);
        Assert.False(reopened.Editor.CanUndo);
        Assert.False(reopened.Session.Details.IsKaraokeEnabled);
        Assert.True(reopened.Editor.Redo());
        Assert.Same(restored, reopened.Editor.Snapshot);
        Assert.True(reopened.Session.Details.IsKaraokeEnabled);
    }

    [Fact]
    public async Task DisabledTextEditingRemapsWholeGraphemesAndRestoresExactTimingAndVisualOverrides()
    {
        var document = DetailedKaraokeDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SetVisualState(KaraokeVisualState.ACTIVE));
        details.SetKaraokeEnabled(false);
        var disabled = context.Editor.Snapshot;
        details.EditText(0, 1, "👨‍👩‍👧‍👦");
        Assert.Same(disabled, context.Editor.Snapshot);
        Assert.True(details.TryCommit(), details.Error);
        var edited = context.Editor.Snapshot;
        var editedLine = edited.Subtitles[0];
        Assert.Equal("👨‍👩‍👧‍👦e\u0301👩‍💻", editedLine.Text);
        Assert.False(details.IsKaraokeEnabled);
        Assert.Empty(editedLine.Karaoke);
        var boundaries = StringInfo.ParseCombiningCharacters(editedLine.Text).Append(editedLine.Text.Length).ToArray();
        var expected = line.Karaoke.Select((clip, index) => clip with
        {
            Utf16Start = boundaries[index],
            Utf16Length = boundaries[index + 1] - boundaries[index]
        }).ToArray();
        Assert.Equal(expected, editedLine.InactiveKaraoke.ToArray());
        var expectedStyles = line.KaraokeStyleSpans.Select((span, index) => span with
        {
            Utf16Start = boundaries[index],
            Utf16Length = boundaries[index + 1] - boundaries[index]
        }).ToArray();
        Assert.Equal(expectedStyles, editedLine.KaraokeStyleSpans.ToArray());
        Assert.Equal(line.InlineSpans[0] with { Utf16Start = boundaries[1] }, Assert.Single(editedLine.InlineSpans));
        Assert.Equal(line.KaraokeStyle, editedLine.KaraokeStyle);
        details.SetKaraokeEnabled(true);
        var restored = context.Editor.Snapshot;
        Assert.True(details.IsKaraokeEnabled);
        Assert.Equal(expected, restored.Subtitles[0].Karaoke.ToArray());
        Assert.Empty(restored.Subtitles[0].InactiveKaraoke);
        Assert.Equal(editedLine.Text, restored.Subtitles[0].Text);
        Assert.Equal(editedLine.InlineSpans, restored.Subtitles[0].InlineSpans);
        Assert.Equal(editedLine.KaraokeStyleSpans, restored.Subtitles[0].KaraokeStyleSpans);
        Assert.True(context.Editor.Undo());
        Assert.Same(edited, context.Editor.Snapshot);
        Assert.False(details.IsKaraokeEnabled);
        Assert.True(context.Editor.Undo());
        Assert.Same(disabled, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task LegacyHighlightCommandsShareRestorationAndDoNotCreateTransactionsForTheSamePreset()
    {
        var document = DetailedKaraokeDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        context.Session.CreateKaraoke(line.KaraokeStyle!.PresetId);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        context.Session.ClearKaraoke();
        var disabled = context.Editor.Snapshot;
        Assert.Equal(line.Karaoke.AsEnumerable(), disabled.Subtitles[0].InactiveKaraoke.AsEnumerable());
        Assert.Equal(line.KaraokeStyle, disabled.Subtitles[0].KaraokeStyle);
        context.Session.CreateKaraoke(line.KaraokeStyle.PresetId);
        var restored = context.Editor.Snapshot;
        Assert.Equal(line.Karaoke.AsEnumerable(), restored.Subtitles[0].Karaoke.AsEnumerable());
        Assert.Empty(restored.Subtitles[0].InactiveKaraoke);
        Assert.Equal(line.KaraokeStyle, restored.Subtitles[0].KaraokeStyle);
        context.Session.CreateKaraoke(line.KaraokeStyle.PresetId);
        Assert.Same(restored, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(disabled, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument DetailedKaraokeDocument()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Text = "甲e\u0301👩‍💻",
            InlineSpans = [new(1, 2, new() { FontFamily = "Noto Sans SC", FontSize = 27, Bold = true })],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.Parse("c50798d5-28a9-497b-9930-93e2a5bb3bd3"), "Saved HDR", new()
            {
                Fill = new(4.123456789012345, -0.123456789012345, 2.75, 0.7312345678901234),
                Stroke = new(0.25, 2, 0.5),
                StrokeWidth = 3.123456789012345,
                ShadowColor = new(0.5, 0.25, 1.5, 0.6),
                ShadowOffset = new(-1.123456789012345, 2.123456789012345),
                ShadowBlur = 1.5123456789012345
            }),
            KaraokeStyleSpans =
            [
                new(0, 1, new() { Fill = new(4.75, 0.25, 0.5, 0.7), StrokeWidth = 0 },
                    new() { Fill = new(0.25, 0.5, 2.75), ShadowOffset = new(-2.5, 1.25) }),
                new(1, 2, new() { Stroke = new(0.25, 3.75, 0.5), ShadowBlur = 2.125 },
                    new() { Stroke = new(1.5, 0.25, 0.5), StrokeWidth = 1.125 }),
                new(3, 5, new() { ShadowOffset = new(-3.125, 5.75), ShadowBlur = 1.625 },
                    new() { ShadowColor = new(0.5, 0.25, 1.75, 0.4) })
            ],
            Karaoke =
            [
                new(0, 1, new(1, 7), new(5, 6), new(3.123456789012345, -0.1, 0.75, 0.6))
                {
                    Id = Guid.Parse("5ff32a44-fc9d-4657-b4b2-724810ce3b0e"),
                    HighlightKind = KaraokeHighlightKind.STEP
                },
                new(1, 2, new(11, 12), new(17, 9), new(0.5, 2.123456789012345, 0.25))
                {
                    Id = Guid.Parse("a877c0e1-4a67-41d9-83df-662861f11b91"),
                    HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
                },
                new(3, 5, new(19, 9), new(31, 11), new(0.25, 0.5, 4.123456789012345))
                {
                    Id = Guid.Parse("d9fd843a-6f9e-47de-a9e1-47db3bd6a3ec"),
                    HighlightKind = KaraokeHighlightKind.SWEEP
                }
            ]
        };
        return document with { Subtitles = [line] };
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(4), Karaoke =
            [new(0, 1, MediaTime.Zero, new(1), SceneColor.White), new(1, 1, new(1), new(2), SceneColor.White)]
        };
        return new() { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
