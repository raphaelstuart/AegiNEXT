using AegiNext.Core.Editing;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ProjectModelTests
{
    [Fact]
    public void EmptyProjectIsValidAndKeepsExactFrameRate()
    {
        var project = new ProjectDocument { FrameRate = new(30000, 1001) };
        ProjectValidator.Validate(project);
        Assert.Equal(new MediaRatio(30000, 1001), project.FrameRate);
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("/root/image.png")]
    [InlineData("C:/image.png")]
    [InlineData("assets//image.png")]
    [InlineData("assets/./image.png")]
    [InlineData("assets\\image.png")]
    public void ManagedAssetPathsCannotEscapeOrDependOnPlatformSeparators(string path)
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateRelativePath(path));
    }

    [Fact]
    public void OnlyMediaMayReferenceExternalFiles()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: "/videos/source.mkv");
        ProjectValidator.Validate(new() { Assets = [asset] });
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Assets = [asset with { Kind = ProjectAssetKind.IMAGE }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Assets = [asset with { RelativePath = "assets/source.mkv" }] }));
    }

    [Fact]
    public void ExternalReferencesSurviveCrossPlatformDocumentsButRequireRebindingForResolution()
    {
        var external = OperatingSystem.IsWindows() ? "/Volumes/Media/source.mkv" : "C:\\Media\\source.mkv";
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: external);
        ProjectValidator.Validate(new() { Assets = [asset] });
        Assert.Throws<NotSupportedException>(() => ProjectAssetLocation.Resolve(asset, Path.GetTempPath()));
    }

    [Fact]
    public void SubtitleRequiresExactlyOneMatchingLayerAndNonemptyInterval()
    {
        var line = new SubtitleLine { Start = new(1, 3), End = new(2, 3), Text = "Hello" };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End };
        var project = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        ProjectValidator.Validate(project);
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with { Layers = [] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with { Layers = [layer, layer with { Id = Guid.NewGuid() }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with { Layers = [layer with { End = new(1) }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with { Subtitles = [line with { End = line.Start }] }));
    }

    [Fact]
    public void KaraokeCannotSplitSurrogateOrCombiningTextElements()
    {
        var line = new SubtitleLine { Text = "😀e\u0301", Karaoke = [new(0, 2, new(0), new(1), SceneColor.White)] };
        var layer = new ProjectLayer { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End };
        var project = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        ProjectValidator.Validate(project);
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with
        {
            Subtitles = [line with { Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] }]
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with
        {
            Subtitles = [line with { Karaoke = [new(2, 1, new(0), new(1), SceneColor.White)] }]
        }));
    }

    [Fact]
    public void LinearHighlightIsPreservedButNonfiniteColorsAndOutOfRangeOpacityFail()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), Fill = new(4, -0.1, 2, 0.5) };
        ProjectValidator.Validate(new() { Layers = [layer] });
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer with { Fill = new(double.NaN, 0, 0) }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer with { Opacity = 1.1 }] }));
    }

    [Fact]
    public void DuplicateTrackPropertiesOrTimesAreRejected()
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0), new(new(1), 1)]);
        var layer = new ProjectLayer { Tracks = [track] };
        ProjectValidator.Validate(new() { Layers = [layer] });
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer with { Tracks = [track, track] }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [layer with { Tracks = [track with { Keyframes = [new(new(0), 0), new(new(0), 1)] }] }]
        }));
    }

    [Fact]
    public void EvaluationUsesHalfOpenTimeAndPreservesNestedComposition()
    {
        var child = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 40, 20), Start = new(1), End = new(2) };
        var group = new ProjectLayer { Children = [child], Transform = new(X: 30), Blend = BlendMode.MULTIPLY };
        var project = new ProjectDocument { Layers = [group] };
        Assert.Empty(Assert.Single(SceneEvaluator.Evaluate(project, new(0))).Children);
        var atStart = Assert.Single(SceneEvaluator.Evaluate(project, new(1)));
        Assert.Equal(BlendMode.MULTIPLY, atStart.Source.Blend);
        Assert.Equal(30, atStart.Transform.X);
        Assert.Equal(child.Id, Assert.Single(atStart.Children).Source.Id);
        Assert.Empty(Assert.Single(SceneEvaluator.Evaluate(project, new(2))).Children);
    }

    [Theory]
    [InlineData(KeyframeInterpolation.HOLD, 0)]
    [InlineData(KeyframeInterpolation.LINEAR, 25)]
    [InlineData(KeyframeInterpolation.EASE_IN, 6.25)]
    [InlineData(KeyframeInterpolation.EASE_OUT, 43.75)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT, 15.625)]
    public void KeyframesHoldBoundariesAndUseSpecifiedInterpolation(KeyframeInterpolation interpolation, double expected)
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 0, interpolation), new(new(1), 100)]);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(-1)));
        Assert.Equal(expected, SceneEvaluator.EvaluateScalarTrack(track, new(1, 4)), 9);
        Assert.Equal(100, SceneEvaluator.EvaluateScalarTrack(track, new(1)));
        Assert.Equal(100, SceneEvaluator.EvaluateScalarTrack(track, new(2)));
    }

    [Fact]
    public void MotionPathAddsLocalPositionAndOrientation()
    {
        var path = new PathGeometry(new(0, 0), [new(new(0, 30), new(0, 60), new(0, 90))]);
        var layer = new ProjectLayer { Transform = new(X: 10), MotionPath = new(path, new(3), true) };
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(new ProjectDocument { Layers = [layer] }, new(3, 2)));
        Assert.Equal(10, evaluated.Transform.X);
        Assert.Equal(45, evaluated.Transform.Y, 9);
        Assert.Equal(90, evaluated.Transform.Rotation, 6);
    }

    [Fact]
    public void PreparedSceneRetainsItsImmutableSnapshotAcrossDocumentEdits()
    {
        var layer = new ProjectLayer { Opacity = 0.5 };
        var original = new ProjectDocument { Layers = [layer] };
        var prepared = new PreparedProjectScene(original);
        var changed = original with { Layers = [layer with { Opacity = 0.8 }] };
        Assert.Same(original, prepared.Document);
        Assert.Equal(0.5, Assert.Single(SceneEvaluator.Evaluate(prepared, new(0))).Opacity);
        Assert.Equal(0.8, Assert.Single(SceneEvaluator.Evaluate(changed, new(0))).Opacity);
    }

    [Fact]
    public void NullJsonArrayItemsAndMalformedUnicodeAreRejectedAtTheBoundary()
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Assets = [null!] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [null!] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateText("incomplete \uD800"));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateText("\uDC00"));
    }
}
