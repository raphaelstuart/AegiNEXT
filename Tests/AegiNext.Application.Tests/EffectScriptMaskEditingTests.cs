using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class EffectScriptMaskEditingTests
{
    [Fact]
    public void ScriptCannotReplaceAnOrderedMaskTrackAndLeavesUndoUntouched()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "ordered");
        initial.SetClipMask(id, new RectangleClipMask { BottomRight = new(100, 100) });
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT);
        initial.SetAnimationTransform(id, target, new ScenePoint(100, 100),
            new(Guid.NewGuid(), new(0), new(2), new ScenePoint(200, 100), 2));
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        var script = EffectScriptParser.Parse("""
            effect "ordered-target" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-rectangle-bottom-right base
                at 1 mask-rectangle-bottom-right offset(50, 0)
            end
            """);

        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(id, script));

        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ScriptReplacesOnlyCompleteTargetsAndMissingNodesLeaveHistoryUntouched()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "morph");
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(100, 30) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] };
        initial.SetClipMask(id, mask);
        var other = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id),
            [new(new(0), second.Position), new(new(2), new ScenePoint(100, 80))]);
        initial.UpdateLayer(id, layer => layer with { Tracks = [other] });
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        var script = EffectScriptParser.Parse("""
            effect "one-node" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-node(1,1).position base power(2)
                at 1 mask-node(1,1).position offset(50, 0)
            end
            """);

        editor.ApplyEffectScript(id, script);

        Assert.Same(mask, editor.Snapshot.Layers[0].Mask);
        Assert.Same(other, editor.Snapshot.Layers[0].Tracks.Single(track => track.Target == other.Target));
        Assert.Equal(2, editor.Snapshot.Layers[0].Tracks.Length);
        var applied = editor.Snapshot;
        var invalid = EffectScriptParser.Parse("""
            effect "missing-node" version 1
            short-clip compress
            segment all flex 1
                at 0 mask-node(1,3).position base
                at 1 mask-node(1,3).position offset(50, 0)
            end
            """);
        Assert.Throws<EffectScriptException>(() => editor.ApplyEffectScript(id, invalid));
        Assert.Same(applied, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }
}
