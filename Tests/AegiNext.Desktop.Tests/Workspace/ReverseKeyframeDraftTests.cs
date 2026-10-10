using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ReverseKeyframeDraftTests
{
    [Fact]
    public async Task EditingAKeyframeValuePreservesTheReversedCurveAndItsCroppedPhase()
    {
        var frame = Frame();
        await using var context = new WorkspaceSessionTestContext(Document(frame));
        await context.InitializeAsync();
        var session = context.Session;
        var layer = context.Editor.Snapshot.Layers[0];
        session.SelectCue(layer.SubtitleId!.Value);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION, frame.Time, frame.Time)));

        session.ViewModel.Effects.KeyframeValueText = "15";
        Assert.True(session.TryCommitDrafts(false));

        var changed = Assert.Single(session.SelectedLayer!.Tracks[0].Keyframes);
        Assert.Equal(15, changed.Value.Scalar);
        Assert.True(changed.Reverse);
        Assert.Equal(frame.CurveStart, changed.CurveStart);
        Assert.Equal(frame.CurveEnd, changed.CurveEnd);
        Assert.Equal(frame.Exponent, changed.Exponent);
    }

    [Fact]
    public async Task SelectingANewInterpolationResetsTheReversedCurveAndItsCroppedPhase()
    {
        var frame = Frame();
        await using var context = new WorkspaceSessionTestContext(Document(frame));
        await context.InitializeAsync();
        var session = context.Session;
        var layer = context.Editor.Snapshot.Layers[0];
        session.SelectCue(layer.SubtitleId!.Value);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION, frame.Time, frame.Time)));

        session.ViewModel.Effects.Interpolation = (int)KeyframeInterpolation.EASE_OUT;
        Assert.True(session.TryCommitDrafts(false));

        var changed = Assert.Single(session.SelectedLayer!.Tracks[0].Keyframes);
        Assert.Equal(KeyframeInterpolation.EASE_OUT, changed.Interpolation);
        Assert.False(changed.Reverse);
        Assert.Equal(0, changed.CurveStart);
        Assert.Equal(1, changed.CurveEnd);
    }

    private static Keyframe Frame()
    {
        return new(new(1), 10, KeyframeInterpolation.POWER) { Exponent = 2.5, CurveStart = 0.2, CurveEnd = 0.8, Reverse = true };
    }

    private static ProjectDocument Document(Keyframe frame)
    {
        var line = new SubtitleLine { Text = "Curve", End = new(2) };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End, Tracks = [new(AnimationProperty.ROTATION, [frame])] }]
        };
    }
}
