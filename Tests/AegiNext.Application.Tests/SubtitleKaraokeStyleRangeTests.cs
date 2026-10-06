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
        Assert.Same(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(new ScenePoint(9.25, 2.123456789123), result.Karaoke[1].ActiveStyle!.ShadowOffset);
        Assert.Equal(new ScenePoint(9.25, 7.123456789123), result.Karaoke[2].ActiveStyle!.ShadowOffset);
        Assert.Equal(line.KaraokeStyle, result.KaraokeStyle);
        Assert.Equal(line.InlineSpans, result.InlineSpans);
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End, clip.InactiveStyle, clip.HighlightKind)),
            result.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End, clip.InactiveStyle, clip.HighlightKind)));
        Assert.Null(result.Karaoke[2].ActiveStyle!.Fill);
        Assert.Equal(line.KaraokeStyle!.Fill, KaraokeVisualStyleResolver.ResolveActive(line.Style, result.KaraokeStyle, result.Karaoke[2]).Fill);
        Assert.Equal(result.Karaoke.ToArray(), ProjectStore.Deserialize(ProjectStore.Serialize(applied)).Subtitles[0].Karaoke.ToArray());
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
    }

    [Fact]
    public void ClearingASelectedOverrideRestoresTheLineDefaultAndKeepsUntimedTextUntimed()
    {
        var document = Document();
        var line = document.Subtitles[0];
        var cleared = ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(document, line.Id, 1, 2);
        Assert.Null(cleared.Subtitles[0].Karaoke[1].ActiveStyle);
        Assert.Same(line.Karaoke[0], cleared.Subtitles[0].Karaoke[0]);
        Assert.Same(line.Karaoke[2], cleared.Subtitles[0].Karaoke[2]);
        Assert.Same(cleared, ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(cleared, line.Id, 4, 1,
            new() { Fill = SceneColor.Black }));
        Assert.Equal(3, cleared.Subtitles[0].Karaoke.Length);
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
        Assert.Same(document, ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, id, 0, 0, new() { Fill = SceneColor.Black }));
    }

    [Fact]
    public void JoinedEmojiRemainOneCharacterWhileUntimedCharactersRemainOutsideTheEdit()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Text = "Ae\u0301👩‍💻Z",
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White),
                new(1, 2, new(1), new(2), SceneColor.White), new(3, 5, new(2), new(3), SceneColor.White)]
        };
        document = document with { Subtitles = [line] };
        var changed = ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, line.Id, 3, 6,
            new() { Fill = SceneColor.Transparent });
        Assert.Equal(3, changed.Subtitles[0].Karaoke.Length);
        Assert.Same(line.Karaoke[0], changed.Subtitles[0].Karaoke[0]);
        Assert.Same(line.Karaoke[1], changed.Subtitles[0].Karaoke[1]);
        Assert.Equal(SceneColor.Transparent, changed.Subtitles[0].Karaoke[2].ActiveStyle!.Fill);
        Assert.Equal(line.Karaoke[2].Id, changed.Subtitles[0].Karaoke[2].Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(
            document, line.Id, 4, 4));
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
            Karaoke = [new(0, 1, MediaTime.Zero, new(1), SceneColor.White),
                new(1, 2, new(1), new(2), SceneColor.White)
                {
                    ActiveStyle = new() { ShadowOffset = new(3, 2.123456789123) },
                    InactiveStyle = new() { Fill = SceneColor.Transparent }, HighlightKind = KaraokeHighlightKind.STEP
                }, new(3, 1, new(2), new(3), SceneColor.White)]
        };
        return new() { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
