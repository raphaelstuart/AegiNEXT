using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests.Rendering;

public sealed class PreviewPreparationMemoryTests
{
    [Fact]
    public void RetainedPixelBudgetIncludesTheCompositingBackgroundAndCountsSharedImagesOnce()
    {
        var catalog = new PreviewFrameCatalog();
        var document = new ProjectDocument();
        using var converter = new ProjectPreviewConverter(() => new(document, Path.GetTempPath()), previewFrames: catalog);
        var background = new SdrVideoFrame(1, 1, [1, 2, 3, 255]);
        var composite = new SdrVideoFrame(1, 1, [4, 5, 6, 255]);
        catalog.Register(composite, background, document);
        Assert.Equal(8, converter.GetRetainedBytes(composite));
        catalog.Register(background, background, document);
        Assert.Equal(4, converter.GetRetainedBytes(background));
    }
}
