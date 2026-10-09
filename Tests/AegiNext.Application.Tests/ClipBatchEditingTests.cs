using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ClipBatchEditingTests
{
    [Fact]
    public void AdjacentAndMixedClipsShiftTogetherWithOneHistoryEntryAndUnchangedContent()
    {
        var setup = new ProjectEditor();
        var first = setup.AddSubtitle(new(0), new(2), "AB");
        var second = setup.AddSubtitle(new(2), new(4), "next");
        var otherTrack = setup.AddTrack("Other");
        var third = setup.AddSubtitle(new(0), new(2), "parallel", otherTrack);
        var untouched = setup.AddSubtitle(new(10), new(12), "untouched");
        setup.UpdateSubtitle(first, line => line with
        {
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(1), new(2), SceneColor.White)]
        });
        setup.SetKeyframe(first, AnimationProperty.OPACITY, new(new(1), 0.5));
        var shape = new ProjectLayer
        {
            TrackId = setup.AddTrack("Shape"), Kind = LayerKind.SHAPE, Start = new(1, 2), End = new(3, 2),
            Shape = new(ShapeKind.RECTANGLE, 30, 40), AnimationOffset = new(1, 3)
        };
        setup.AddLayer(shape);
        var before = setup.Snapshot;
        var editor = new ProjectEditor(before);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ShiftClips([first, second, third, shape.Id, first], new(1));

        Assert.Equal(1, changes);
        foreach (var id in new[] { first, second, third })
        {
            var original = before.Subtitles.Single(line => line.Id == id);
            var shifted = editor.Snapshot.Subtitles.Single(line => line.Id == id);
            Assert.Equal(original with { Start = original.Start + new MediaTime(1), End = original.End + new MediaTime(1) }, shifted);
            var layer = editor.Snapshot.Layers.Single(value => value.SubtitleId == id);
            var originalLayer = before.Layers.Single(value => value.SubtitleId == id);
            Assert.Equal(originalLayer with { Start = originalLayer.Start + new MediaTime(1), End = originalLayer.End + new MediaTime(1) }, layer);
        }
        Assert.Same(before.Subtitles.Single(line => line.Id == untouched), editor.Snapshot.Subtitles.Single(line => line.Id == untouched));
        Assert.Equal(shape with { Start = new(3, 2), End = new(5, 2) }, editor.Snapshot.Layers.Single(layer => layer.Id == shape.Id));
        var after = editor.Snapshot;
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Fact]
    public void FinalCollisionRejectsEveryMemberButMovingBeyondANeighbourIsAllowed()
    {
        var setup = new ProjectEditor();
        var first = setup.AddSubtitle(new(0), new(2), "first");
        setup.AddSubtitle(new(2), new(4), "neighbour");
        var shape = new ProjectLayer { TrackId = setup.AddTrack("Shape"), Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 30, 30) };
        setup.AddLayer(shape);
        var before = setup.Snapshot;
        var editor = new ProjectEditor(before);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<InvalidDataException>(() => editor.ShiftClips([first, shape.Id], new(1)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, changes);
        editor.ShiftClips([first, shape.Id], new(6));
        Assert.Equal(new MediaTime(6), editor.Snapshot.Subtitles[0].Start);
        Assert.Equal(new MediaTime(6), editor.Snapshot.Layers.Single(layer => layer.Id == shape.Id).Start);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void EmptyAndZeroShiftsDoNotPublishOrChangeHistory()
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(1), new(2), "one");
        var editor = new ProjectEditor(setup.Snapshot);
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ShiftClips([], new(1));
        editor.ShiftClips([id], MediaTime.Zero);
        editor.RemoveClips([]);

        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void InvalidMemberRejectsTheBatchWithoutChangingRedo()
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(1), new(2), "one");
        var editor = new ProjectEditor(setup.Snapshot);
        editor.ShiftClips([id], new(1));
        Assert.True(editor.Undo());
        var before = editor.Snapshot;

        Assert.Throws<KeyNotFoundException>(() => editor.ShiftClips([id, Guid.NewGuid()], new(1)));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveClips([id, Guid.NewGuid()]));
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void NegativeResultRejectsEveryMemberAndTouchingZeroIsValid()
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(1), new(2), "one");
        var editor = new ProjectEditor(setup.Snapshot);
        var before = editor.Snapshot;

        Assert.Throws<InvalidDataException>(() => editor.ShiftClips([id], new(-2)));
        Assert.Same(before, editor.Snapshot);
        editor.ShiftClips([id], new(-1));
        Assert.Equal(MediaTime.Zero, editor.Snapshot.Subtitles[0].Start);
        Assert.Equal(new MediaTime(1), editor.Snapshot.Subtitles[0].End);
    }

    [Fact]
    public void MixedDeletionRemovesSubtitleRowsAndLayersTogetherAndKeepsResources()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/font.ttf");
        var imageAsset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/image.png");
        var setup = new ProjectEditor(new() { Assets = [font, imageAsset] });
        var first = setup.AddSubtitle(new(0), new(2), "first");
        var remaining = setup.AddSubtitle(new(2), new(4), "remaining");
        var shape = new ProjectLayer { TrackId = setup.AddTrack("Shape"), Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 20) };
        var image = new ProjectLayer { TrackId = setup.AddTrack("Image"), Kind = LayerKind.IMAGE, Image = new(imageAsset.Id, 30, 40) };
        setup.AddLayer(shape);
        setup.AddLayer(image);
        var before = setup.Snapshot;
        var editor = new ProjectEditor(before);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.RemoveClips([first, shape.Id, image.Id, first]);

        Assert.Equal(remaining, Assert.Single(editor.Snapshot.Subtitles).Id);
        Assert.Equal(remaining, Assert.Single(editor.Snapshot.Layers).SubtitleId);
        Assert.Equal(before.Assets, editor.Snapshot.Assets);
        Assert.Equal(before.Tracks, editor.Snapshot.Tracks);
        Assert.Equal(1, changes);
        var after = editor.Snapshot;
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }
}
