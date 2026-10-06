using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests.Rendering;

public sealed class PreviewQualityRenderingTests
{
    [Theory]
    [InlineData(PreviewQuality.LOWEST, false, 568, 320)]
    [InlineData(PreviewQuality.LOWEST, true, 568, 320)]
    [InlineData(PreviewQuality.LOW, false, 960, 540)]
    [InlineData(PreviewQuality.LOW, true, 960, 540)]
    [InlineData(PreviewQuality.STANDARD, false, 1280, 720)]
    [InlineData(PreviewQuality.STANDARD, true, 960, 540)]
    [InlineData(PreviewQuality.HIGH, false, 1920, 1080)]
    [InlineData(PreviewQuality.HIGH, true, 960, 540)]
    public void PreviewBudgetNeverExceedsSelectedQualityDuringInteraction(PreviewQuality quality, bool interactive,
        int expectedWidth, int expectedHeight)
    {
        var options = PreviewQualityOptions.Get(quality, interactive);

        Assert.Equal(expectedWidth, options.MaximumWidth);
        Assert.Equal(expectedHeight, options.MaximumHeight);
    }

    [PreviewQualityNativeFact]
    public async Task RealConversionAndCompositionHonorSelectedQualityAndRestoreItAfterInteraction()
    {
        using var fixture = await PreviewQualityFixture.CreateAsync(1920, 1080);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 0);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var originalPlane = frame.CopyPlane(0);
        var catalog = new PreviewFrameCatalog();
        var document = new ProjectDocument();
        var state = new ProjectPreviewState(document, fixture.DirectoryPath);
        using var converter = new ProjectPreviewConverter(() => state, previewFrames: catalog);
        var low = converter.Convert(frame);
        AssertSize(low, 960, 540);
        AssertSize(catalog.FindBackground(low)!, 960, 540);

        state = state with { Quality = PreviewQuality.HIGH, QualityRevision = 1 };
        var high = converter.Convert(frame);
        AssertSize(high, 1920, 1080);
        AssertSize(catalog.FindBackground(high)!, 1920, 1080);
        Assert.Equal(1, catalog.FindIdentity(high)!.QualityRevision);
        Assert.Same(document, catalog.FindIdentity(high)!.Document);

        state = state with { IsInteractive = true, TargetTime = new MediaTime(1), QualityRevision = 2 };
        var interactive = converter.Convert(frame);
        AssertSize(interactive, 960, 540);
        AssertSize(catalog.FindBackground(interactive)!, 960, 540);
        Assert.Equal(new MediaTime(1), catalog.FindIdentity(interactive)!.Time);
        Assert.True(catalog.FindIdentity(interactive)!.Interactive);

        state = state with { IsInteractive = false, TargetTime = null, QualityRevision = 3 };
        var restored = converter.Convert(frame);
        AssertSize(restored, 1920, 1080);
        Assert.False(catalog.FindIdentity(restored)!.Interactive);
        state = state with { Quality = PreviewQuality.STANDARD, QualityRevision = 4 };
        var standard = converter.Convert(frame);
        AssertSize(standard, 1280, 720);
        AssertSize(catalog.FindBackground(standard)!, 1280, 720);
        Assert.Equal(4, catalog.FindIdentity(standard)!.QualityRevision);

        state = state with { Quality = PreviewQuality.LOWEST, QualityRevision = 5 };
        var veryLow = converter.Convert(frame);
        AssertSize(veryLow, 568, 320);
        AssertSize(catalog.FindBackground(veryLow)!, 568, 320);
        Assert.Equal(5, catalog.FindIdentity(veryLow)!.QualityRevision);

        state = state with { IsInteractive = true, TargetTime = new MediaTime(1), QualityRevision = 6 };
        var veryLowInteractive = converter.Convert(frame);
        AssertSize(veryLowInteractive, 568, 320);
        AssertSize(catalog.FindBackground(veryLowInteractive)!, 568, 320);
        Assert.True(catalog.FindIdentity(veryLowInteractive)!.Interactive);

        state = state with { IsInteractive = false, TargetTime = null, QualityRevision = 7 };
        var veryLowRestored = converter.Convert(frame);
        AssertSize(veryLowRestored, 568, 320);
        Assert.False(catalog.FindIdentity(veryLowRestored)!.Interactive);
        Assert.Equal(0, catalog.FindIdentity(low)!.QualityRevision);
        Assert.Equal(originalPlane, frame.CopyPlane(0));
        Assert.Equal(1920, frame.Info.Width);
        Assert.Equal(1080, frame.Info.Height);
    }

    [PreviewQualityNativeFact]
    public async Task ASourceBelowTheSelectedBudgetIsNotUpscaled()
    {
        using var fixture = await PreviewQualityFixture.CreateAsync(320, 180);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 0);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var state = new ProjectPreviewState(new(), fixture.DirectoryPath, Quality: PreviewQuality.LOWEST);
        var catalog = new PreviewFrameCatalog();
        using var converter = new ProjectPreviewConverter(() => state, previewFrames: catalog);
        var presented = converter.Convert(frame);
        AssertSize(presented, 320, 180);
        AssertSize(catalog.FindBackground(presented)!, 320, 180);
    }

    private static void AssertSize(SdrVideoFrame frame, int width, int height)
    {
        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(width * height * 4, frame.Pixels.Length);
    }
}
