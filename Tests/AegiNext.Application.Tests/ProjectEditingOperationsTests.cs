using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ProjectEditingOperationsTests
{
    [Fact]
    public void SplitPreservesOriginalLeftIdentityAndAnimationPhaseOnBothSides()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "ab😀");
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(0, 0), KeyframeInterpolation.EASE_IN));
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(4), new ScenePoint(100, 0)));
        var original = editor.Snapshot;
        var split = ProjectEditingOperations.SplitSubtitle(original, id, new(2), 2);
        Assert.Equal(id, split.Subtitles[0].Id);
        Assert.NotEqual(id, split.Subtitles[1].Id);
        Assert.Equal("ab", split.Subtitles[0].Text);
        Assert.Equal("😀", split.Subtitles[1].Text);
        Assert.Equal(new MediaTime(2), split.Subtitles[0].End);
        Assert.Equal(new MediaTime(2), split.Subtitles[1].Start);
        Assert.Equal(new MediaTime(2), split.Layers[1].AnimationOffset);
        Assert.Equal(Assert.Single(SceneEvaluator.Evaluate(original, new(1))).Transform,
            Assert.Single(SceneEvaluator.Evaluate(split, new(1))).Transform);
        Assert.Equal(Assert.Single(SceneEvaluator.Evaluate(original, new(3))).Transform,
            Assert.Single(SceneEvaluator.Evaluate(split, new(3))).Transform);
        Assert.Single(original.Subtitles);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    [InlineData(2, 3)]
    public void SplitRejectsTimeEdgesTextEdgesAndSurrogateBoundaries(int seconds, int offset)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "ab😀");
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(seconds), offset));
    }

    [Fact]
    public void SplitRejectsCombiningMarkAndWhitespaceOnlyFragments()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "e\u0301 x");
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(2), 1));
        editor.UpdateSubtitle(id, line => line with { Text = "  x" });
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(2), 2));
    }

    [Fact]
    public void SplitPartitionsCanonicalKaraokeWithoutChangingExistingGlyphTimesOrIds()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "abcd");
        editor.UpdateSubtitle(id, line => line with { Karaoke = [new(0, 4, new(1), new(3), new(4, 1, 0))] });
        var original = editor.Snapshot.Subtitles[0].Karaoke;
        var split = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(2), 2);
        Assert.Equal(original.Take(2), split.Subtitles[0].Karaoke);
        Assert.Equal(original.Skip(2).Select(clip => clip with { Utf16Start = clip.Utf16Start - 2 }), split.Subtitles[1].Karaoke);
        var clipped = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(1, 2), 2);
        Assert.Equal(original.Select(clip => (clip.Id, clip.Start, clip.End)),
            clipped.Subtitles.SelectMany(line => line.Karaoke).Select(clip => (clip.Id, clip.Start, clip.End)));
    }

    [Fact]
    public void SplitWholeKaraokeWordsRebasesOnlyTextIndicesAndKeepsContentTime()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(5), new(9), "abcd");
        editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [new(0, 2, new(0), new(2), SceneColor.White), new(2, 2, new(2), new(4), SceneColor.White)]
        });
        var split = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(7), 2);
        Assert.Equal(new MediaTime(2), split.Layers[1].AnimationOffset);
        Assert.Equal(2, split.Subtitles[1].Karaoke.Length);
        Assert.Equal(0, split.Subtitles[1].Karaoke[0].Utf16Start);
        Assert.Equal(new MediaTime(2), split.Subtitles[1].Karaoke[0].Start);
    }

    [Fact]
    public void MergeUsesFirstStyleAndOffsetsSecondHighlightsAcrossTheGap()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(1), new(2), "a");
        var second = editor.AddSubtitle(new(3), new(4), "b");
        editor.UpdateSubtitle(first, line => line with
        {
            Style = line.Style with { FontSize = 31 }, Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)]
        });
        editor.UpdateSubtitle(second, line => line with
        {
            Style = line.Style with { FontSize = 77 }, Karaoke = [new(0, 1, new(0), new(1), new(1, 0, 0))]
        });
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second);
        var line = Assert.Single(merged.Subtitles);
        Assert.Equal(first, line.Id);
        Assert.Equal("a\nb", line.Text);
        Assert.Equal(31, line.Style.FontSize);
        Assert.Equal(new MediaTime(4), line.End);
        Assert.Equal(new MediaTime(2), line.Karaoke[1].Start);
        Assert.Equal(new MediaTime(3), line.Karaoke[1].End);
        Assert.Equal(2, line.Karaoke[1].Utf16Start);
        Assert.Equal(SceneColor.White, line.Karaoke[0].HighlightColor);
        Assert.Single(merged.Layers);
    }

    [Fact]
    public void MergeRebasesClippedKaraokeAfterExtendedLeftEdge()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(1), "a");
        var second = editor.AddSubtitle(new(1), new(2), "b");
        editor.UpdateSubtitle(second, line => line with { Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] });
        editor.SetSubtitleTiming(first, new(-100), new(1), TimelineEditMode.CROP);
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second, "");
        Assert.Equal(new MediaTime(-100), Assert.Single(merged.Layers).AnimationOffset);
        Assert.Equal(new MediaTime(1), Assert.Single(Assert.Single(merged.Subtitles).Karaoke).Start);
    }

    [Fact]
    public void MergeKeepsCompletedHighlightsThatPrecedeTheCroppedVisibleStart()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(4), "a");
        var second = editor.AddSubtitle(new(4), new(5), "b");
        editor.UpdateSubtitle(first, line => line with { Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] });
        editor.SetSubtitleTiming(first, new(2), new(4), TimelineEditMode.CROP);
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second, "");
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(merged, new(2)));
        Assert.Equal(new MediaTime(2), evaluated.LocalTime);
        Assert.Equal(new MediaTime(1), Assert.Single(evaluated.Subtitle!.Karaoke).End);
    }

    [Fact]
    public void MergeRejectsOverlapNonadjacencyAndEffectsInsteadOfDroppingData()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "a");
        var otherTrack = editor.AddSubtitleTrack("Other");
        var second = editor.AddSubtitle(new(1), new(3), "b", otherTrack);
        var third = editor.AddSubtitle(new(4), new(5), "c");
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second));
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, third));
        editor.SetSubtitleTiming(second, new(2), new(3), TimelineEditMode.CROP);
        editor.MoveSubtitleToTrack(second, SubtitleTrack.DEFAULT_TRACK_ID);
        editor.SetKeyframe(second, AnimationProperty.OPACITY, new(new(1), 0.5));
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second));
    }

    [Fact]
    public void GroupUngroupAndReorderPreserveSiblingOrderAndStableIds()
    {
        var first = Shape("first");
        var second = Shape("second");
        var third = Shape("third");
        var original = new ProjectDocument { Layers = [first, second, third] };
        var grouped = ProjectEditingOperations.GroupLayers(original, [second.Id, first.Id], "pair");
        var group = grouped.Layers[0];
        Assert.Equal(first.Id, group.Children[0].Id);
        Assert.Equal(second.Id, group.Children[1].Id);
        Assert.Equal(third.Id, grouped.Layers[1].Id);
        var moved = ProjectEditingOperations.MoveLayer(grouped, second.Id, 0);
        Assert.Equal(second.Id, moved.Layers[0].Children[0].Id);
        var ungrouped = ProjectEditingOperations.UngroupLayer(moved, group.Id);
        Assert.Equal(second.Id, ungrouped.Layers[0].Id);
        Assert.Equal(first.Id, ungrouped.Layers[1].Id);
        Assert.Equal(third.Id, ungrouped.Layers[2].Id);
        Assert.Equal(first.Id, original.Layers[0].Id);
    }

    [Fact]
    public void GroupRejectsNoncontiguousCrossParentAndNonNormalBlendSelections()
    {
        var first = Shape("first");
        var second = Shape("second");
        var third = Shape("third");
        var original = new ProjectDocument { Layers = [first, second, third] };
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.GroupLayers(original, [first.Id, third.Id]));
        var grouped = ProjectEditingOperations.GroupLayers(original, [first.Id, second.Id]);
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.GroupLayers(grouped, [first.Id, third.Id]));
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.GroupLayers(original with
        {
            Layers = [first, second with { Blend = BlendMode.MULTIPLY }, third]
        }, [first.Id, second.Id]));
    }

    [Fact]
    public void UngroupRejectsGroupEffectsOrTimeClipping()
    {
        var child = Shape("child");
        var group = new ProjectLayer { Children = [child], Opacity = 0.5 };
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.UngroupLayer(new() { Layers = [group] }, group.Id));
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.UngroupLayer(new()
        {
            Layers = [group with { Opacity = 1, End = new(1) }]
        }, group.Id));
    }

    [Fact]
    public void RecursiveRemovalDeletesOnlyOwnedSubtitleRowsAndIsOneUndoTransaction()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(1), "a");
        var second = editor.AddSubtitle(new(1), new(2), "b");
        var third = editor.AddSubtitle(new(2), new(3), "c");
        editor.Apply("Group", document => ProjectEditingOperations.GroupLayers(document, [first, second]));
        var original = editor.Snapshot;
        var groupId = original.Layers[0].Id;
        editor.Apply("Remove group", document => ProjectEditingOperations.RemoveLayer(document, groupId));
        Assert.Equal(third, Assert.Single(editor.Snapshot.Subtitles).Id);
        Assert.Equal(third, Assert.Single(editor.Snapshot.Layers).Id);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Fact]
    public void InvalidTreeEditsLeaveSourceSnapshotUnchanged()
    {
        var layer = Shape("only");
        var project = new ProjectDocument { Layers = [layer] };
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectEditingOperations.MoveLayer(project, layer.Id, 1));
        Assert.Throws<KeyNotFoundException>(() => ProjectEditingOperations.RemoveLayer(project, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.GroupLayers(project, [layer.Id, layer.Id]));
        Assert.Same(project, ProjectEditingOperations.MoveLayer(project, layer.Id, 0));
        Assert.Equal(layer.Id, Assert.Single(project.Layers).Id);
    }

    private static ProjectLayer Shape(string name)
    {
        return new() { Name = name, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 30) };
    }
}
