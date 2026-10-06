using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class MaskAnimationTargetEditingTests
{
    [Fact]
    public void NodeKeyframeDraftChangesOnlyItsCompleteTarget()
    {
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        var line = new SubtitleLine { End = new(2) };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), [new(new(0), first.Position)]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id), [new(new(0), second.Position)])
            ]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };

        var changed = WorkspaceDraftOperations.SetKeyframe(document, layer.Id,
            new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), new(new(0), new ScenePoint(50, 60)));

        Assert.Equal(new ScenePoint(50, 60), changed.Layers[0].Tracks[0].Keyframes[0].Value.Vector);
        Assert.Same(layer.Tracks[1], changed.Layers[0].Tracks[1]);
    }

    [Fact]
    public void OrderedTransformCannotBeSilentlyReplacedByKeyframeDraft()
    {
        var line = new SubtitleLine { End = new(2) };
        var track = new AnimationTrack(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, [])
        {
            InitialValue = new ScenePoint(0, 0),
            Transforms = [new(Guid.NewGuid(), new(0), new(1), new ScenePoint(10, 20), 2)]
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(100, 100) }, Tracks = [track]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };

        Assert.Throws<InvalidOperationException>(() => WorkspaceDraftOperations.SetKeyframe(document, layer.Id,
            track.Target, new(new(1), new ScenePoint(50, 60))));
        Assert.Same(track, document.Layers[0].Tracks[0]);
    }
}
