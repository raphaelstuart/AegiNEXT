using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class AnimationTrackClearingTests
{
    [Fact]
    public void ClearingMixedFlatClipsPreservesStaticDataAndIsOneUndo()
    {
        var setup = new ProjectEditor();
        var subtitleId = setup.AddSubtitle(new(0), new(4), "Highlight");
        var untouchedId = setup.AddSubtitle(new(5), new(7), "Untouched");
        setup.SetSubtitleKaraokeEnabled(subtitleId, true);
        setup.SetClipMask(subtitleId, new RectangleClipMask { BottomRight = new(100, 100) });
        setup.UpdateLayer(subtitleId, layer => layer with
        {
            AnimationOffset = new(1), Transform = layer.Transform with { Position = new(20, 30) },
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(4)),
            Tracks = [new(AnimationProperty.POSITION, [new(new(1), new ScenePoint(10, 20))])]
        });
        setup.SetKeyframe(untouchedId, AnimationProperty.OPACITY, new(new(0), 0.5));
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Tracks = [new(AnimationProperty.OPACITY, [])
            {
                InitialValue = 1, Transforms = [new(Guid.NewGuid(), new(0), new(2), 0.5)]
            }]
        };
        var shapeTrack = new ProjectTrack { Name = "Shape" };
        shape = shape with { TrackId = shapeTrack.Id };
        var original = setup.Snapshot with { Tracks = setup.Snapshot.Tracks.Add(shapeTrack), Layers = setup.Snapshot.Layers.Add(shape) };
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ClearAnimationTracks([subtitleId, shape.Id, subtitleId]);

        var cleared = editor.Snapshot;
        var subtitle = cleared.Layers[0];
        Assert.Empty(subtitle.Tracks);
        Assert.Empty(cleared.Layers[2].Tracks);
        Assert.Same(original.Subtitles[0], cleared.Subtitles[0]);
        Assert.NotEmpty(cleared.Subtitles[0].Karaoke);
        Assert.Same(original.Layers[0].Transform, subtitle.Transform);
        Assert.Same(original.Layers[0].MotionPath, subtitle.MotionPath);
        Assert.Same(original.Layers[0].Mask, subtitle.Mask);
        Assert.Equal(original.Layers[0].AnimationOffset, subtitle.AnimationOffset);
        Assert.Same(original.Layers[1], cleared.Layers[1]);
        Assert.Equal(shape.Id, cleared.Layers[2].Id);
        Assert.Same(shape.Transform, cleared.Layers[2].Transform);
        Assert.Equal(1, changes);
        ProjectValidator.Validate(cleared);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(cleared, editor.Snapshot);
    }

    [Fact]
    public void PropertyClearingRemovesEveryNodeAndBothTrackRepresentations()
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(0), new(4), "Nodes");
        var first = new MaskNode();
        var second = new MaskNode();
        setup.SetClipMask(id, new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        setup.SetKeyframe(id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id),
            new(new(0), new ScenePoint(10, 20)));
        setup.SetAnimationTransform(id, new(AnimationProperty.MASK_NODE_POSITION, second.Id), new ScenePoint(0, 0),
            new(Guid.NewGuid(), new(0), new(2), new ScenePoint(30, 40)));
        setup.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.5));
        var original = setup.Snapshot;
        var editor = new ProjectEditor(original);

        editor.ClearAnimationTracks([id], AnimationProperty.MASK_NODE_POSITION);

        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(editor.Snapshot.Layers[0].Tracks).Property);
        Assert.Same(original.Layers[0].Mask, editor.Snapshot.Layers[0].Mask);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void SceneClipPropertyClearingKeepsOtherClipAnimation()
    {
        var child = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])] };
        var otherTrack = new ProjectTrack { Name = "Other" };
        child = child with { TrackId = otherTrack.Id };
        var selected = child with { Id = Guid.NewGuid(), TrackId = ProjectTrack.DEFAULT_TRACK_ID,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.75)])] };
        var original = new ProjectDocument { Tracks = [ProjectTrack.Default, otherTrack], Layers = [selected, child] };
        var editor = new ProjectEditor(original);

        editor.ClearAnimationTracks([selected.Id], AnimationProperty.OPACITY);

        Assert.Empty(editor.Snapshot.Layers[0].Tracks);
        Assert.Same(child, editor.Snapshot.Layers[1]);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownMemberRejectsTheWholeBatchAndPreservesRedo(bool propertyOnly)
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])] };
        var original = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(original);
        editor.UpdateLayer(layer.Id, value => value with { Name = "Redo fixture" });
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<KeyNotFoundException>(() =>
        {
            if (propertyOnly)
            {
                editor.ClearAnimationTracks([layer.Id, Guid.NewGuid()], AnimationProperty.OPACITY);
            }
            else
            {
                editor.ClearAnimationTracks([layer.Id, Guid.NewGuid()]);
            }
        });

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void EmptyOrUnaffectedSelectionsPreserveSnapshotHistoryAndNotifications()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20) };
        var original = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(original);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ClearAnimationTracks(Array.Empty<Guid>());
        editor.ClearAnimationTracks([layer.Id, layer.Id]);
        editor.ClearAnimationTracks([layer.Id], AnimationProperty.OPACITY);

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void InvalidPropertyIsRejectedBeforeAnyChange()
    {
        var clip = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10) };
        var original = new ProjectDocument { Layers = [clip] };
        var editor = new ProjectEditor(original);

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ClearAnimationTracks([clip.Id], (AnimationProperty)int.MaxValue));

        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }
}
