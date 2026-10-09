using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ProjectEditorTests
{
    [Fact]
    public void EqualSubtitleAndFlatLayerEditsPreserveSavedSnapshotAndRedoHistory()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "same");
        var child = Assert.Single(initial.Snapshot.Layers);
        var document = initial.Snapshot;
        var editor = new ProjectEditor(document);
        editor.UpdateSubtitle(id, line => line with { Text = "changed" });
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.UpdateSubtitle(id, line => line with { Style = line.Style with { FontSize = line.Style.FontSize } });
        editor.UpdateLayer(child.Id, layer => layer with { Transform = layer.Transform with { X = layer.Transform.X } });
        editor.SetSubtitleTiming(id, new(0), new(2), TimelineEditMode.CROP);
        editor.SetSubtitleTiming(id, new(0), new(2), TimelineEditMode.STRETCH);
        editor.ShiftSubtitle(id, MediaTime.Zero);
        editor.Apply("same document", snapshot => snapshot with { Name = snapshot.Name });

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, changes);
        Assert.True(editor.Redo());
        Assert.Equal("changed", Assert.Single(editor.Snapshot.Subtitles).Text);
    }

    [Fact]
    public void EqualKeyframeDoesNotCreateTransactionButChangedInterpolationDoes()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "same");
        initial.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1, 3), 0.5));
        var document = initial.Snapshot;
        var editor = new ProjectEditor(document);

        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(2, 6), 0.5));
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);

        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1, 3), 0.5, KeyframeInterpolation.HOLD));
        Assert.True(editor.CanUndo);
        Assert.Equal(KeyframeInterpolation.HOLD, Assert.Single(Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks).Keyframes).Interpolation);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
    }

    [Fact]
    public void HistoryIsAtomicBranchableAndRestoresSavedSnapshotIdentity()
    {
        var editor = new ProjectEditor(historyLimit: 2);
        var original = editor.Snapshot;
        var id = editor.AddSubtitle(new(0), new(2), "one");
        Assert.True(editor.HasUnsavedChanges);
        Assert.Single(editor.Snapshot.Subtitles);
        Assert.Single(editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
        Assert.True(editor.Redo());
        Assert.Equal(id, Assert.Single(editor.Snapshot.Subtitles).Id);
        Assert.True(editor.Undo());
        editor.AddSubtitle(new(2), new(3), "branch");
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void ValidationAndDelegateFailureLeaveSnapshotAndHistoryUntouched()
    {
        var editor = new ProjectEditor();
        var original = editor.Snapshot;
        Assert.Throws<InvalidDataException>(() => editor.Apply("bad", document => document with { Width = 0 }));
        Assert.Throws<InvalidOperationException>(() => editor.Apply("bad", _ => throw new InvalidOperationException("failure")));
        Assert.Throws<InvalidOperationException>(() => editor.Apply("reentry", document => { editor.Undo(); return document; }));
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void SavingAnOlderSnapshotDoesNotMarkNewerChangesSaved()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(1), "first");
        var saving = editor.Snapshot;
        editor.AddSubtitle(new(1), new(2), "second");
        editor.MarkSaved(saving);
        Assert.True(editor.HasUnsavedChanges);
        Assert.True(editor.Undo());
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void HistoryCapacityLimitsUndoWithoutAffectingCurrentDocument()
    {
        var editor = new ProjectEditor(historyLimit: 2);
        editor.AddSubtitle(new(0), new(1), "1");
        editor.AddSubtitle(new(1), new(2), "2");
        editor.AddSubtitle(new(2), new(3), "3");
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.False(editor.Undo());
        Assert.Single(editor.Snapshot.Subtitles);
    }

    [Fact]
    public void CroppingPreservesOriginalEasePhaseWhileStretchingRetimesExactly()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "phase");
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(0, 0), KeyframeInterpolation.EASE_IN));
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(4), new ScenePoint(100, 0)));
        var before = Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(2))).Transform.X;
        editor.SetSubtitleTiming(id, new(1), new(3), TimelineEditMode.CROP);
        Assert.Equal(new MediaTime(1), Assert.Single(editor.Snapshot.Layers).AnimationOffset);
        Assert.Equal(before, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(2))).Transform.X);
        editor.SetSubtitleTiming(id, new(1), new(5), TimelineEditMode.STRETCH);
        var layer = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(new MediaTime(2), layer.AnimationOffset);
        Assert.Equal(new MediaTime(6), Assert.Single(layer.Tracks).Keyframes[^1].Time);
        Assert.Equal(before, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(3))).Transform.X);
        editor.ShiftSubtitle(id, new(7));
        Assert.Equal(before, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(10))).Transform.X);
    }

    [Fact]
    public void StretchRetimesKaraokeAndPathsWithoutMillisecondQuantization()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(1), "a");
        editor.UpdateSubtitle(id, line => line with { Karaoke = [new(0, 1, new(0), new(1, 3), SceneColor.White)] });
        editor.UpdateLayer(id, layer => layer with
        {
            MotionPath = new(new(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]), new(1, 3))
        });
        editor.SetSubtitleTiming(id, new(0), new(1, 7), TimelineEditMode.STRETCH);
        Assert.Equal(new MediaTime(1, 21), Assert.Single(Assert.Single(editor.Snapshot.Subtitles).Karaoke).End);
        Assert.Equal(new MediaTime(1, 21), Assert.Single(editor.Snapshot.Layers).MotionPath!.Duration);
    }

    [Fact]
    public void KeyframeReplacementAndPresetApplicationPreserveStableIdentity()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(1), "text");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.2));
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.5));
        Assert.Equal(0.5, Assert.Single(Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks).Keyframes).Value.Scalar);
        var preset = new EffectPreset(Guid.NewGuid(), "fade", [new(AnimationProperty.OPACITY, [new(new(0), 0), new(new(1), 1)])]);
        editor.ApplyPreset(id, preset);
        Assert.Equal(id, Assert.Single(editor.Snapshot.Layers).Id);
        Assert.Equal(id, Assert.Single(editor.Snapshot.Subtitles).Id);
        editor.RemoveSubtitle(id);
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.Empty(editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Equal(id, Assert.Single(editor.Snapshot.Layers).Id);
    }

    [Fact]
    public void OutsideKeyframeFailsAtomicallyAndShortPresetIsClippedWithUndo()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(10), new(12), "short");
        var before = editor.Snapshot;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(3), 0.5)));
        Assert.Same(before, editor.Snapshot);
        var preset = new EffectPreset(Guid.NewGuid(), "long", [new(AnimationProperty.OPACITY,
            [new(new(0), 0, KeyframeInterpolation.EASE_OUT), new(new(4), 1)])]);
        editor.ApplyPreset(id, preset);
        var track = Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks);
        Assert.Equal(new MediaTime(2), track.Keyframes[^1].Time);
        Assert.Equal(0.75, track.Keyframes[^1].Value.Scalar);
        Assert.Equal(0.5, track.Keyframes[0].CurveEnd);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(track.Keyframes.ToArray(), Assert.Single(Assert.Single(restored.Layers).Tracks).Keyframes.ToArray());
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Equal(track, Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks));
    }

    [Fact]
    public void IndependentLayerTrimAndMoveUseTheSameContentClockAndPreserveEaseSamples()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(10), End = new(18),
            Tracks = [new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0), KeyframeInterpolation.EASE_IN_OUT), new(new(8), new ScenePoint(100, 0))])]
        };
        var editor = new ProjectEditor(new() { Layers = [layer] });
        var before = Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(13))).Transform.X;
        editor.SetLayerTiming(layer.Id, new(12), new(16), TimelineEditMode.CROP);
        var clipped = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(new MediaTime(2), clipped.AnimationOffset);
        Assert.Equal(new MediaTime(2), clipped.Tracks[0].Keyframes[0].Time);
        Assert.Equal(new MediaTime(6), clipped.Tracks[0].Keyframes[^1].Time);
        Assert.Equal(before, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(13))).Transform.X, 10);
        editor.ShiftLayer(layer.Id, new(7));
        var moved = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(clipped.Tracks, moved.Tracks);
        Assert.Equal(before, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(20))).Transform.X, 10);
        Assert.True(editor.Undo());
        Assert.Equal(clipped, Assert.Single(editor.Snapshot.Layers));
    }

    [Fact]
    public void PresetStartsAtVisibleOriginOfAnAlreadyCroppedTarget()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10),
            Start = new(10), End = new(12), AnimationOffset = new(5)
        };
        var editor = new ProjectEditor(new() { Layers = [layer] });
        var preset = new EffectPreset(Guid.NewGuid(), "fade", [new(AnimationProperty.OPACITY,
            [new(new(0), 0, KeyframeInterpolation.EASE_OUT), new(new(4), 1)])]);
        editor.ApplyPreset(layer.Id, preset);
        var track = Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks);
        Assert.Equal(new MediaTime(5), track.Keyframes[0].Time);
        Assert.Equal(new MediaTime(7), track.Keyframes[^1].Time);
        Assert.Equal(0, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(10))).Opacity);
        Assert.Equal(0.4375, Assert.Single(SceneEvaluator.Evaluate(editor.Snapshot, new(11))).Opacity, 10);
        Assert.True(editor.Undo());
        Assert.Equal(layer, Assert.Single(editor.Snapshot.Layers));
        Assert.True(editor.Redo());
        Assert.Equal(track, Assert.Single(Assert.Single(editor.Snapshot.Layers).Tracks));
    }
}
