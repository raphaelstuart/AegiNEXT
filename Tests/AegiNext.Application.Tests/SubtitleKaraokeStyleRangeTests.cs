using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitleKaraokeStyleRangeTests
{
    [Fact]
    public void SparseVisualEditsPreserveEachCharactersOtherShadowComponentAndExactFields()
    {
        var document = Document();
        var line = document.Subtitles[0];
        var editor = new ProjectEditor(document);
        editor.ApplySubtitleKaraokeStyleRange(line.Id, 1, 3, new() { ShadowX = 9.25, StrokeWidth = 0 });
        var applied = editor.Snapshot;
        var result = applied.Subtitles[0];
        Assert.Equal(new ScenePoint(9.25, 2.123456789123), Active(result, 1)!.ShadowOffset);
        Assert.Equal(new ScenePoint(9.25, 7.123456789123), Active(result, 3)!.ShadowOffset);
        Assert.Equal(line.KaraokeStyle, result.KaraokeStyle);
        Assert.Equal(line.InlineSpans, result.InlineSpans);
        Assert.Same(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(line.Karaoke, result.Karaoke);
        Assert.Equal(Inactive(line, 1), Inactive(result, 1));
        Assert.Null(Active(result, 3)!.Fill);
        Assert.Equal(line.KaraokeStyle!.Fill, KaraokeVisualStyleResolver.ResolveActive(line.Style,
            result.KaraokeStyle, result.Karaoke[2], Active(result, 3)).Fill);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(applied)).Subtitles[0];
        Assert.Equal(result.KaraokeStyleSpans.ToArray(), restored.KaraokeStyleSpans.ToArray());
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
    }

    [Fact]
    public void ClearingOneStateRestoresTheDefaultAndPreservesTheOtherStateAndTiming()
    {
        var document = Document();
        var line = document.Subtitles[0];
        var cleared = ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(document, line.Id, 1, 2);
        var result = cleared.Subtitles[0];
        Assert.Null(Active(result, 1));
        Assert.Equal(Inactive(line, 1), Inactive(result, 1));
        Assert.Equal(line.Karaoke, result.Karaoke);
        Assert.Equal(line.KaraokeStyle!.ShadowOffset, KaraokeVisualStyleResolver.ResolveActive(line.Style,
            result.KaraokeStyle, result.Karaoke[1], Active(result, 1)).ShadowOffset);
        var changed = ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(cleared, line.Id, 4, 1,
            new() { Fill = SceneColor.Black });
        Assert.Equal(SceneColor.Black, Active(changed.Subtitles[0], 4)!.Fill);
        Assert.Equal(result.Karaoke, changed.Subtitles[0].Karaoke);
    }

    [Fact]
    public void PartialTextSelectionInsideAGroupEditsBothStatesWithoutSplittingTime()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Karaoke = [new(0, 5, new(1, 3), new(10, 3), SceneColor.White)], KaraokeStyleSpans = []
        };
        document = document with { Subtitles = [line] };
        var editor = new ProjectEditor(document);
        editor.ApplySubtitleKaraokeStyleRange(line.Id, 1, 2, new() { Fill = SceneColor.Transparent, StrokeWidth = 0 });
        editor.ApplySubtitleKaraokeStyleRange(line.Id, 3, 1, KaraokeVisualState.INACTIVE,
            new() { Fill = SceneColor.Black, StrokeWidth = 9 });
        var result = editor.Snapshot.Subtitles[0];
        Assert.Same(line.Karaoke[0], Assert.Single(result.Karaoke));
        Assert.Null(Active(result, 0));
        Assert.Equal(SceneColor.Transparent, Active(result, 1)!.Fill);
        Assert.Equal(9, Inactive(result, 3)!.StrokeWidth);
        Assert.Null(Inactive(result, 1));
        editor.ClearSubtitleKaraokeStyleRange(line.Id, 3, 1, KaraokeVisualState.INACTIVE);
        Assert.Null(Inactive(editor.Snapshot.Subtitles[0], 3));
        Assert.Equal(SceneColor.Transparent, Active(editor.Snapshot.Subtitles[0], 1)!.Fill);
    }

    [Fact]
    public void DisabledAndUntimedTextKeepIndependentStylesThroughSaveAndRestore()
    {
        var document = Document();
        var line = document.Subtitles[0];
        var editor = new ProjectEditor(document);
        editor.SetSubtitleKaraokeEnabled(line.Id, false);
        editor.ApplySubtitleKaraokeStyleRange(line.Id, 0, 5, new() { Fill = SceneColor.Transparent });
        editor.ApplySubtitleKaraokeStyleRange(line.Id, 4, 1, KaraokeVisualState.INACTIVE,
            new() { StrokeWidth = 0 });
        var disabled = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Empty(disabled.Subtitles[0].Karaoke);
        Assert.Equal(line.Karaoke.ToArray(), disabled.Subtitles[0].InactiveKaraoke.ToArray());
        var restored = ProjectEditingOperations.SetSubtitleKaraokeEnabled(disabled, line.Id, true).Subtitles[0];
        Assert.Equal(disabled.Subtitles[0].KaraokeStyleSpans, restored.KaraokeStyleSpans);
        Assert.Equal(line.Karaoke.ToArray(), restored.Karaoke.ToArray());
        Assert.Equal(SceneColor.Transparent, Active(restored, 4)!.Fill);
    }

    [Fact]
    public void CharacterStyleRangesRejectPartialUnicodeGraphemesAndInvalidValues()
    {
        var document = Document();
        var id = document.Subtitles[0].Id;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, id, 1, 1, new() { Fill = SceneColor.Black }));
        Assert.Throws<InvalidDataException>(() =>
            ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, id, 0, 1, new() { StrokeWidth = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(
            document, id, 0, 1, (KaraokeVisualState)99));
        Assert.Same(document, ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, id, 0, 0,
            new() { Fill = SceneColor.Black }));
    }

    [Fact]
    public void JoinedEmojiRemainOneCharacterAndUntimedCharactersKeepDormantStyles()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Text = "Ae\u0301👩‍💻Z", KaraokeStyleSpans = [],
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White),
                new(1, 2, new(1), new(2), SceneColor.White), new(3, 5, new(2), new(3), SceneColor.White)]
        };
        document = document with { Subtitles = [line] };
        var changed = ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, line.Id, 3, 6,
            new() { Fill = SceneColor.Transparent }).Subtitles[0];
        Assert.Equal(line.Karaoke, changed.Karaoke);
        Assert.Equal(SceneColor.Transparent, Active(changed, 3)!.Fill);
        Assert.Equal(SceneColor.Transparent, Active(changed, 8)!.Fill);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(
            document, line.Id, 4, 4));
    }

    private static KaraokeVisualStyleOverride? Active(SubtitleLine line, int offset)
    {
        return KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.ACTIVE);
    }

    private static KaraokeVisualStyleOverride? Inactive(SubtitleLine line, int offset)
    {
        return KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.INACTIVE);
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "Ae\u0301BX", End = new(5),
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Default", new()
            {
                Fill = new(4.123456789123, 0.25, 1), ShadowOffset = new(-4, 7.123456789123)
            }),
            KaraokeStyleSpans = [new(1, 2, new() { ShadowOffset = new(3, 2.123456789123) },
                new() { Fill = SceneColor.Transparent })],
            Karaoke = [new(0, 1, MediaTime.Zero, new(1), SceneColor.White),
                new(1, 2, new(1), new(2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.STEP },
                new(3, 1, new(2), new(3), SceneColor.White)]
        };
        return new() { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
