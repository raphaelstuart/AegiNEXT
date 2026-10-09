using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

/// <summary>验证左右和垂直边距的独立数值、默认值及自动定位。</summary>
public sealed class SubtitleMarginsTests
{
    /// <summary>默认构造保持旧样式的 40 像素边距。</summary>
    [Fact]
    public void DefaultsKeepFortyPixelMarginsAndValuesCompareStructurally()
    {
        Assert.Equal(new SubtitleMargins(40, 40, 40), new SubtitleMargins());
        Assert.Equal(new SubtitleMargins(40, 40, 40), new SubtitleStyle().Margins);
        Assert.Equal(new SubtitleMargins(12, 27, 9), new SubtitleMargins(12, 27, 9));
        Assert.NotEqual(new SubtitleMargins(12, 27, 9), new SubtitleMargins(27, 12, 9));
    }

    /// <summary>九宫格的水平居中位于左右边距定义的可用区域中心。</summary>
    [Theory]
    [InlineData(TextAlignment.TOP_LEFT, 0, 0, 12, 9)]
    [InlineData(TextAlignment.TOP_CENTER, 0.5, 0, -7.5, 9)]
    [InlineData(TextAlignment.TOP_RIGHT, 1, 0, -27, 9)]
    [InlineData(TextAlignment.MIDDLE_LEFT, 0, 0.5, 12, 0)]
    [InlineData(TextAlignment.MIDDLE_CENTER, 0.5, 0.5, -7.5, 0)]
    [InlineData(TextAlignment.MIDDLE_RIGHT, 1, 0.5, -27, 0)]
    [InlineData(TextAlignment.BOTTOM_LEFT, 0, 1, 12, -9)]
    [InlineData(TextAlignment.BOTTOM_CENTER, 0.5, 1, -7.5, -9)]
    [InlineData(TextAlignment.BOTTOM_RIGHT, 1, 1, -27, -9)]
    public void AlignmentUsesEachMarginWithoutLosingTheHorizontalCenter(TextAlignment alignment,
        double anchorX, double anchorY, double offsetX, double offsetY)
    {
        var position = SubtitlePosition.FromAlignment(alignment, new(12, 27, 9));

        Assert.Equal(new ScenePoint(anchorX, anchorY), position.Anchor);
        Assert.Equal(position.Anchor, position.Pivot);
        Assert.Equal(new ScenePoint(offsetX, offsetY), position.Offset);
    }

    /// <summary>每个边距分别保持原有有限非负像素预算。</summary>
    [Theory]
    [InlineData(-0.01)]
    [InlineData(32768.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EveryMarginMustRemainFiniteAndWithinItsPixelBudget(double value)
    {
        foreach (var margins in new[]
                 {
                     new SubtitleMargins(value, 40, 40),
                     new SubtitleMargins(40, value, 40),
                     new SubtitleMargins(40, 40, value)
                 })
        {
            Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { Margins = margins }));
        }
    }

    /// <summary>零和原有最大预算均为有效的独立边距。</summary>
    [Fact]
    public void ZeroAndMaximumMarginsRemainValidIndependently()
    {
        ProjectValidator.ValidateSubtitleStyle(new() { Margins = new(0, 32768, 9.25) });
        ProjectValidator.ValidateSubtitleStyle(new() { Margins = default });
    }
}
