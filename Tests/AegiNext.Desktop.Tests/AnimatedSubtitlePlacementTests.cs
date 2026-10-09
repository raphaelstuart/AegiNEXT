using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Tests;

public sealed class AnimatedSubtitlePlacementTests
{
    [Fact]
    public void PositionMeasurementUsesAnimatedSpacingAtTheContentEndpointAndCachesOnlyItsEffectiveValue()
    {
        var line = new SubtitleLine { Text = "ABC DEF", End = new(2), Style = new() { FontFamily = "sans-serif", FontSize = 24 } };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Tracks = [new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(2), 12)])]
        };
        var document = new ProjectDocument { Width = 640, Height = 360, Subtitles = [line], Layers = [layer] };
        using var resolver = new LayerPlacementResolver();

        var first = resolver.Resolve(document, Path.GetTempPath(), layer, new(0));
        var end = resolver.Resolve(document, Path.GetTempPath(), layer, new(2));
        var held = resolver.Resolve(document, Path.GetTempPath(), layer, new(3));
        var staticResult = resolver.Resolve(document, Path.GetTempPath(), layer);

        Assert.Null(first.Error);
        Assert.Null(end.Error);
        Assert.True(end.Geometry!.GlyphSize.X > first.Geometry!.GlyphSize.X);
        Assert.Same(end, held);
        Assert.Equal(first.Geometry.GlyphSize, staticResult.Geometry!.GlyphSize);
        Assert.Equal(0, line.Style.LetterSpacing);
    }
}
