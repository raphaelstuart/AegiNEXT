using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using Avalonia.Media;

namespace AegiNext.Desktop.Tests;

public sealed class SceneColorConversionTests
{
    [Fact]
    public void SrgbPickerUsesLinearUnassociatedColorAndKeepsAlphaIndependent()
    {
        var scene = SceneColorConversion.FromColor(Color.FromArgb(64, 128, 255, 0));
        Assert.InRange(scene.Red, 0.2158604, 0.2158606);
        Assert.Equal(1, scene.Green);
        Assert.Equal(0, scene.Blue);
        Assert.Equal(64 / 255d, scene.Alpha, 7);
        Assert.True(scene.Green > scene.Alpha);
        var transparentRed = SceneColorConversion.FromColor(Color.FromArgb(0, 255, 0, 0));
        Assert.Equal(1, transparentRed.Red);
        Assert.Equal(0, transparentRed.Alpha);
    }

    [Fact]
    public void EveryByteValueRoundTripsWithoutColorOrAlphaDrift()
    {
        for (var value = 0; value <= byte.MaxValue; value++)
        {
            var color = Color.FromArgb((byte)value, (byte)value, (byte)(255 - value), 128);
            Assert.Equal(color, SceneColorConversion.ToColor(SceneColorConversion.FromColor(color)));
        }
    }

    [Fact]
    public void PickerDisplayClampsHdrAndNegativeChannelsWithoutMutatingSceneValues()
    {
        var source = new SceneColor(-0.25, 4, 0.0031308, 0.5);
        var color = SceneColorConversion.ToColor(source);
        Assert.Equal(Color.FromArgb(128, 0, 255, 10), color);
        Assert.Equal(-0.25, source.Red);
        Assert.Equal(4, source.Green);
        Assert.Equal(0.0031308, source.Blue);
        Assert.Equal(0.5, source.Alpha);
    }
}
