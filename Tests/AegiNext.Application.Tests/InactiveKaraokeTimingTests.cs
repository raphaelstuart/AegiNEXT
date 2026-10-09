using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class InactiveKaraokeTimingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BothStretchEntrypointsRetimeSavedAndMixedSegmentsWithoutQuantization(bool clipOperation, bool mixed)
    {
        var editor = new ProjectEditor(Document(mixed));
        var before = editor.Snapshot;
        var line = Assert.Single(before.Subtitles);

        Retime(editor, line.Id, new(5), new(36, 7), TimelineEditMode.STRETCH, clipOperation);

        var after = editor.Snapshot;
        var changed = Assert.Single(after.Subtitles);
        Assert.Equal(new MediaTime(5), changed.Start);
        Assert.Equal(new MediaTime(36, 7), changed.End);
        Assert.Equal(line.KaraokeStyle, changed.KaraokeStyle);
        Assert.Equal(line.InlineSpans, changed.InlineSpans);
        var clips = changed.Karaoke.Concat(changed.InactiveKaraoke).OrderBy(clip => clip.Utf16Start).ToArray();
        var originalClips = line.Karaoke.Concat(line.InactiveKaraoke).OrderBy(clip => clip.Utf16Start).ToArray();
        Assert.Equal(originalClips[0] with { Start = new(1, 21), End = new(2, 21) }, clips[0]);
        Assert.Equal(originalClips[1] with { Start = new(2, 21), End = new(1, 7) }, clips[1]);
        Assert.Equal(mixed ? 1 : 0, changed.Karaoke.Length);
        Assert.Equal(mixed ? 1 : 2, changed.InactiveKaraoke.Length);
        Assert.Equal(new MediaTime(2, 77), Assert.Single(after.Layers).AnimationOffset);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BothCropEntrypointsPreserveSavedAndMixedContentClocks(bool clipOperation, bool mixed)
    {
        var editor = new ProjectEditor(Document(mixed));
        var before = editor.Snapshot;
        var line = Assert.Single(before.Subtitles);

        Retime(editor, line.Id, new(21, 4), new(23, 4), TimelineEditMode.CROP, clipOperation);

        var after = editor.Snapshot;
        Assert.Equal(line with { Start = new(21, 4), End = new(23, 4) }, Assert.Single(after.Subtitles));
        Assert.Equal(new MediaTime(19, 44), Assert.Single(after.Layers).AnimationOffset);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void WholeSubtitleAndLayerMovesKeepSavedAndMixedContentClocks(int entrypoint, bool mixed)
    {
        var editor = new ProjectEditor(Document(mixed));
        var before = editor.Snapshot;
        var line = Assert.Single(before.Subtitles);
        var layer = Assert.Single(before.Layers);
        var delta = new MediaTime(11, 7);

        switch (entrypoint)
        {
            case 0:
            {
                editor.ShiftSubtitle(line.Id, delta);
                break;
            }
            case 1:
            {
                editor.ShiftLayer(layer.Id, delta);
                break;
            }
            case 2:
            case 3:
            {
                editor.MoveSubtitleClip(line.Id, new ProjectClipIndex(editor.Snapshot).GetSubtitleTrackId(line.Id), line.Start + delta, line.End + delta,
                    entrypoint == 2 ? TimelineEditMode.CROP : TimelineEditMode.STRETCH, true);
                break;
            }
            default:
            {
                throw new ArgumentOutOfRangeException(nameof(entrypoint));
            }
        }

        var after = editor.Snapshot;
        Assert.Equal(line with { Start = new(46, 7), End = new(53, 7) }, Assert.Single(after.Subtitles));
        Assert.Equal(layer with { Start = new(46, 7), End = new(53, 7) }, Assert.Single(after.Layers));
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoOpTimingEditsPreserveTheSavedSnapshotAndRedoHistory(bool clipOperation)
    {
        var editor = new ProjectEditor(Document(true));
        var before = editor.Snapshot;
        var line = Assert.Single(before.Subtitles);
        Retime(editor, line.Id, new(5), new(7), TimelineEditMode.STRETCH, clipOperation);
        Assert.True(editor.Undo());

        Retime(editor, line.Id, line.Start, line.End, TimelineEditMode.CROP, clipOperation);
        Retime(editor, line.Id, line.Start, line.End, TimelineEditMode.STRETCH, clipOperation);

        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal(new MediaTime(4, 3), editor.Snapshot.Subtitles[0].InactiveKaraoke[0].Start);
        Assert.Equal(new MediaTime(2), editor.Snapshot.Subtitles[0].InactiveKaraoke[0].End);
    }

    private static void Retime(ProjectEditor editor, Guid id, MediaTime start, MediaTime end,
        TimelineEditMode mode, bool clipOperation)
    {
        if (clipOperation)
        {
            editor.MoveSubtitleClip(id, new ProjectClipIndex(editor.Snapshot).GetSubtitleTrackId(id), start, end, mode, false);
        }
        else
        {
            editor.SetSubtitleTiming(id, start, end, mode);
        }
    }

    private static ProjectDocument Document(bool mixed)
    {
        var first = new KaraokeSegment(0, 1, new(1, 3), new(2, 3), new(4.123456789123, 0.25, 1, 0.5))
        {
            ActiveStyle = new() { StrokeWidth = 3.123456789123 },
            InactiveStyle = new() { Fill = SceneColor.Transparent }, HighlightKind = KaraokeHighlightKind.STEP
        };
        var second = new KaraokeSegment(1, 2, new(2, 3), new(1), new(1, 0, 4.234567891234))
        {
            ActiveStyle = new() { ShadowOffset = new(4.234567891234, -2.123456789123), ShadowBlur = 0 },
            InactiveStyle = new() { StrokeWidth = 0 }, HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
        var line = new SubtitleLine
        {
            Text = "Ae\u0301", Start = new(5), End = new(6),
            Karaoke = mixed ? [first] : [], InactiveKaraoke = mixed ? [second] : [first, second],
            InlineSpans = [new(1, 2, new() { Bold = true })],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Saved HDR highlight", new()
            {
                Fill = new(4.123456789123, 0.25, 1), ShadowOffset = new(-4, 7.123456789123)
            })
        };
        return new()
        {
            Subtitles = [line], Layers = [new()
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                Start = line.Start, End = line.End, AnimationOffset = new(2, 11)
            }]
        };
    }
}
