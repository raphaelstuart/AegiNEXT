using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitlePositionTests
{
    [Fact]
    public void NineGridAlignmentProducesNormalizedAnchorPivotAndSignedPixelMargins()
    {
        var position = SubtitlePosition.FromAlignment(TextAlignment.BOTTOM_RIGHT, new(40, 40, 40));

        Assert.Equal(new ScenePoint(1, 1), position.Anchor);
        Assert.Equal(position.Anchor, position.Pivot);
        Assert.Equal(new ScenePoint(-40, -40), position.Offset);
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(0.5, double.PositiveInfinity)]
    public void AnchorAndPivotMustRemainFiniteAndNormalized(double x, double y)
    {
        var style = new SubtitleStyle { Position = new() { Anchor = new(x, y) } };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(style));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(style with
        {
            Position = new() { Pivot = new(x, y) }
        }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1000000001)]
    public void PositionOffsetsUseTheSameFinitePixelBudgetAsProjectGeometry(double offset)
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new()
        {
            Position = new() { Offset = new(offset, 0) }
        }));
    }
}
