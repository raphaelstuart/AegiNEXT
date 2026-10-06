using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests.Rendering;

public sealed class ProjectPreviewFrameTests
{
    [Theory]
    [InlineData(1920, 1080, 1280, 720, 1280, 720)]
    [InlineData(1080, 1920, 1280, 720, 405, 720)]
    [InlineData(1920, 1080, 640, 480, 640, 360)]
    public void ProjectPreviewSizePreservesCanvasAspectWithinTheConvertedVideoBudget(
        int projectWidth, int projectHeight, int videoWidth, int videoHeight, int expectedWidth, int expectedHeight)
    {
        var document = new ProjectDocument { Width = projectWidth, Height = projectHeight };
        var size = ProjectPreviewConverter.GetPreviewSize(document, videoWidth, videoHeight);
        Assert.Equal((expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void SourceFrameAssociationFollowsTheDeliveredCompositeRatherThanTheLatestConversion()
    {
        var frames = new PreviewFrameCatalog();
        var firstVideo = new SdrVideoFrame(1, 1, [0, 80, 0, 255]);
        var firstComposite = new SdrVideoFrame(1, 1, [0, 120, 0, 255]);
        var secondVideo = new SdrVideoFrame(1, 1, [0, 0, 80, 255]);
        var secondComposite = new SdrVideoFrame(1, 1, [0, 0, 120, 255]);
        frames.Register(firstComposite, firstVideo);
        frames.Register(secondComposite, secondVideo);

        Assert.Same(firstVideo, frames.FindBackground(firstComposite));
        Assert.Same(secondVideo, frames.FindBackground(secondComposite));
        Assert.Null(frames.FindBackground(new(1, 1, [0, 0, 0, 255])));
    }
}
