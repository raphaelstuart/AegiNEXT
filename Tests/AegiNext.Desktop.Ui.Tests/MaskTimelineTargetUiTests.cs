using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskTimelineTargetUiTests
{
    [AvaloniaFact]
    public void SelectedNodeTimelineDragMovesOnlyThatTargetsTimeWhileOtherNodesStayVisible()
    {
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        var line = new SubtitleLine { End = new(8) };
        var firstTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        var secondTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id);
        var key = new Keyframe(new(1), first.Position);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
            Tracks = [new(firstTarget, [key]), new(secondTarget, [new(new(1), second.Position)])]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false, SelectedMaskNodeId = first.Id, EffectTarget = firstTarget };
        timeline.SetDocument(document, line.Id, layer);
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = true;
        timeline.KeyframeMoved += (_, e) =>
        {
            Assert.Equal(firstTarget, e.Target);
            Assert.Equal(key.Value, e.NewValue);
            editor.UpdateLayer(layer.Id, item => item with
            {
                Tracks = item.Tracks.Select(track => track.Target == e.Target ? track with
                {
                    Keyframes = [key with { Time = e.NewTime }]
                } : track).ToImmutableArray()
            });
        };
        var window = new Window { Width = 700, Height = 400, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Contains(timeline.KeyframeMarkers, marker => marker.Identity.Target.NodeId == first.Id);
            Assert.Contains(timeline.KeyframeMarkers, marker => marker.Identity.Target.NodeId == second.Id);
            Assert.NotNull(timeline.GetKeyframePoint(layer.Id, secondTarget, new(1), second.Position));
            var point = timeline.GetKeyframePoint(layer.Id, firstTarget, new(1), key.Value)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(80, -50));
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(point + new Vector(80, -50), MouseButton.Left);
            var changed = editor.Snapshot.Layers[0];
            Assert.Equal(new MediaTime(2), Assert.Single(changed.Tracks[0].Keyframes).Time);
            Assert.Equal(first.Position, changed.Tracks[0].Keyframes[^1].Value.Vector);
            Assert.Same(layer.Tracks[1], changed.Tracks[1]);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OrderedTransformEndpointDragRetainsOperationIdentityValueAccelerationAndOrder()
    {
        var line = new SubtitleLine { End = new(8) };
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(3), new ScenePoint(100, 150), 2.5);
        var track = new AnimationTrack(target, []) { InitialValue = new ScenePoint(0, 0), Transforms = [operation] };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(500, 500) }, Tracks = [track]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false, EffectTarget = target };
        timeline.SetDocument(document, line.Id, layer);
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = true;
        timeline.KeyframeMoved += (_, e) =>
        {
            Assert.Equal(operation.Id, e.OperationId);
            Assert.False(e.IsOperationStart);
            editor.SetAnimationTransform(layer.Id, e.Target, track.InitialValue!.Value, operation with { End = e.NewTime });
        };
        var window = new Window { Width = 700, Height = 400, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = timeline.KeyframeMarkers.First(marker => marker.Identity.OperationId == operation.Id && !marker.Identity.IsOperationStart).Position;
            window.MouseDown(point, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(point + new Vector(80, -50));
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(point + new Vector(80, -50), MouseButton.Left);
            var changed = Assert.Single(editor.Snapshot.Layers[0].Tracks[0].Transforms);
            Assert.Equal(operation with { End = new(4) }, changed);
            Assert.Empty(editor.Snapshot.Layers[0].Tracks[0].Keyframes);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
        }
        finally
        {
            window.Close();
        }
    }
}
