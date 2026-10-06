using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class KaraokeLeadingDelayEditingTests
{
    [Fact]
    public void LeadingDelayShiftsEveryClipExactlyAndLeavesTheCueAndStylesUnchanged()
    {
        var document = Document();
        var editor = new ProjectEditor(document);
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        SetLeadingDelay(editor, line.Id, new(11, 4), new(1, 2));
        var changed = editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(13, 4), changed.Karaoke[0].Start);
        var delta = changed.Karaoke[0].Start - line.Karaoke[0].Start;
        for (var index = 0; index < line.Karaoke.Length; index++)
        {
            Assert.Equal(line.Karaoke[index] with { Start = line.Karaoke[index].Start + delta, End = line.Karaoke[index].End + delta }, changed.Karaoke[index]);
        }
        Assert.Equal(line with { Karaoke = changed.Karaoke }, changed);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Equal(changed, editor.Snapshot.Subtitles[0]);
        Assert.True(editor.Undo());
        SetLeadingDelay(editor, line.Id, new(1, 4), new(1, 2));
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void ZeroDelayReturnsToTheVisibleCueOriginWithoutChangingAnyDuration()
    {
        var editor = new ProjectEditor(Document());
        var before = editor.Snapshot.Subtitles[0];
        SetLeadingDelay(editor, before.Id, MediaTime.Zero, new(1, 2));
        var after = editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(1, 2), after.Karaoke[0].Start);
        Assert.Equal(before.Karaoke.Select(clip => clip.End - clip.Start), after.Karaoke.Select(clip => clip.End - clip.Start));
        Assert.Equal(before.Start, after.Start);
        Assert.Equal(before.End, after.End);
    }

    [Fact]
    public void NegativeDelayIsRejectedAndEmptyKaraokeRemainsANoOp()
    {
        var editor = new ProjectEditor(Document());
        var before = editor.Snapshot;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetKaraokeLeadingDelay(
            before.Subtitles[0].Id, new(-1), new(1, 2)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        var empty = before with { Subtitles = [before.Subtitles[0] with { Karaoke = [] }] };
        Assert.Same(empty, ProjectEditingOperations.SetKaraokeLeadingDelay(empty,
            empty.Subtitles[0].Id, new(1), new(1, 2)));
    }

    private static void SetLeadingDelay(ProjectEditor editor, Guid subtitleId, MediaTime delay, MediaTime animationOffset)
    {
        editor.SetKaraokeLeadingDelay(subtitleId, delay, animationOffset);
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "ab", Start = new(5), End = new(9),
            InlineSpans = [new(0, 1, new() { Bold = true })],
            Karaoke = [new(0, 1, new(3, 4), new(7, 4), SceneColor.White),
                new(1, 1, new(2), new(11, 4), new(1, 0, 0)) { ActiveStyle = new() { StrokeWidth = 2 } }]
        };
        return new() { Subtitles = [line], Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
            Start = line.Start, End = line.End, AnimationOffset = new(1, 2) }] };
    }
}
