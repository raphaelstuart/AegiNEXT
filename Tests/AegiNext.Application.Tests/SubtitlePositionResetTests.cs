using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitlePositionResetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetPositionEffectsPreservesPlacementAndOtherLayersWithOneUndo(bool explicitPosition)
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "自然尺寸");
        initial.AddSubtitle(new(4), new(8), "保留另一条字幕");
        if (explicitPosition)
        {
            initial.UpdateSubtitle(id, line => line with
            {
                Style = line.Style with
                {
                    Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.3, 0.4), Offset = new(18, -24) }
                }
            });
        }
        initial.UpdateLayer(id, layer => layer with
        {
            Transform = new() { Position = new(30, 40), Scale = new(2, 3), Rotation = 25 },
            Opacity = 0.75, Blur = 2, Blend = BlendMode.SCREEN,
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(4)),
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(30, 40))]),
                new(AnimationProperty.SCALE, [new(new(0), new ScenePoint(2, 3))]),
                new(AnimationProperty.OPACITY, [new(new(0), 0.75)]),
                new(AnimationProperty.PATH_PROGRESS, [new(new(0), 0), new(new(4), 1)])
            ]
        });
        var before = initial.Snapshot;
        var originalLayer = before.Layers[0];
        var editor = new ProjectEditor(before);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.ResetSubtitlePositionEffects(id);

        var after = editor.Snapshot;
        var layer = after.Layers[0];
        Assert.Equal(before.Subtitles, after.Subtitles);
        Assert.Same(before.Subtitles[0].Style, after.Subtitles[0].Style);
        Assert.Same(before.Layers[1], after.Layers[1]);
        Assert.Equal(originalLayer.Transform with { Position = default }, layer.Transform);
        Assert.Equal(originalLayer.Opacity, layer.Opacity);
        Assert.Equal(originalLayer.Blur, layer.Blur);
        Assert.Equal(originalLayer.Blend, layer.Blend);
        Assert.Equal(originalLayer.Start, layer.Start);
        Assert.Equal(originalLayer.End, layer.End);
        Assert.Null(layer.MotionPath);
        Assert.Equal(originalLayer.Tracks.Where(track => track.Property is AnimationProperty.SCALE or AnimationProperty.OPACITY), layer.Tracks);
        Assert.Equal(1, changes);
        editor.ResetSubtitlePositionEffects(id);
        Assert.Same(after, editor.Snapshot);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Fact]
    public void UnknownSubtitleCannotResetPositionEffectsOrCreateHistory()
    {
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<KeyNotFoundException>(() => editor.ResetSubtitlePositionEffects(Guid.NewGuid()));

        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void RestoreAutomaticPositionClearsOnlyPositionEffectsAndCreatesExactlyOneUndo()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "自然尺寸");
        initial.UpdateSubtitle(id, line => line with { Style = line.Style with { Position = new() { Offset = new(10, 20) } } });
        initial.UpdateLayer(id, layer => layer with
        {
            Transform = new() { Position = new(30, 40), Scale = new(2, 3), Rotation = 25 },
            Opacity = 0.75,
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(4)),
            Tracks =
            [
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(30, 40))]),
                new(AnimationProperty.SCALE, [new(new(0), new ScenePoint(2, 3))]),
                new(AnimationProperty.OPACITY, [new(new(0), 0.75)]),
                new(AnimationProperty.PATH_PROGRESS, [new(new(0), 0), new(new(4), 1)])
            ]
        });
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        editor.ResetSubtitlePosition(id);
        Assert.Null(editor.Snapshot.Subtitles[0].Style.Position);
        var layer = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(default, layer.Transform.Position);
        Assert.Equal(new ScenePoint(2, 3), layer.Transform.Scale);
        Assert.Equal(25, layer.Transform.Rotation);
        Assert.Equal(0.75, layer.Opacity);
        Assert.Null(layer.MotionPath);
        Assert.Equal(new[] { AnimationProperty.SCALE, AnimationProperty.OPACITY }, layer.Tracks.Select(track => track.Property));
        Assert.Equal(1, changes);
        var after = editor.Snapshot;
        editor.ResetSubtitlePosition(id);
        Assert.Same(after, editor.Snapshot);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Throws<KeyNotFoundException>(() => editor.ResetSubtitlePosition(Guid.NewGuid()));
    }
}
