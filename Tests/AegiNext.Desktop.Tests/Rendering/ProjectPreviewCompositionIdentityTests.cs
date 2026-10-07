using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests.Rendering;

public sealed class ProjectPreviewCompositionIdentityTests
{
    [PreviewQualityNativeFact]
    public async Task RealCompositesPreserveOriginsInteractiveTargetsAndQualityRevisionWithoutChangingPriorIdentity()
    {
        using var fixture = await PreviewQualityFixture.CreateAsync(320, 180);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 0);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var sourceTime = (frame.Info.PresentationTimestamp ?? frame.Info.BestEffortTimestamp
            ?? throw new InvalidDataException("The native fixture must have a presentation timestamp.")).ToMediaTime();
        var catalog = new PreviewFrameCatalog();
        var state = new ProjectPreviewState(new(), fixture.DirectoryPath);
        Exception? compositionError = null;
        using var converter = new ProjectPreviewConverter(() => state, error => compositionError = error, catalog);
        var revision = 1L;
        foreach (var origin in new[] { new MediaTime(-2), new MediaTime(3) })
        {
            var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, Path.GetFileName(fixture.MediaPath));
            var document = new ProjectDocument { Assets = [asset], Media = new(asset.Id, 0, null, origin) };
            ProjectValidator.Validate(document);
            var target = new MediaTime(123, 100);
            state = new(document, fixture.DirectoryPath, target, Quality: PreviewQuality.HIGH, QualityRevision: revision++);
            var normal = converter.Convert(frame);
            Assert.Null(compositionError);
            var normalIdentity = Assert.IsType<PreviewFrameRecord>(catalog.FindIdentity(normal));
            Assert.Same(document, normalIdentity.Document);
            Assert.Equal(sourceTime - origin, normalIdentity.Time);
            Assert.False(normalIdentity.Interactive);
            Assert.Equal(state.QualityRevision, normalIdentity.QualityRevision);
            Assert.Same(normalIdentity.Background, catalog.FindBackground(normal));

            state = state with { IsInteractive = true, QualityRevision = revision++ };
            var interactive = converter.Convert(frame);
            Assert.Null(compositionError);
            var interactiveIdentity = Assert.IsType<PreviewFrameRecord>(catalog.FindIdentity(interactive));
            Assert.Same(document, interactiveIdentity.Document);
            Assert.Equal(target, interactiveIdentity.Time);
            Assert.True(interactiveIdentity.Interactive);
            Assert.Equal(state.QualityRevision, interactiveIdentity.QualityRevision);
            Assert.Equal(sourceTime - origin, catalog.FindIdentity(normal)!.Time);
            Assert.False(catalog.FindIdentity(normal)!.Interactive);
            Assert.Equal(normalIdentity.QualityRevision, catalog.FindIdentity(normal)!.QualityRevision);

            var changedDocument = document with { Name = "New composition snapshot" };
            ProjectValidator.Validate(changedDocument);
            state = state with { Document = changedDocument, IsInteractive = false, QualityRevision = revision++ };
            var changed = converter.Convert(frame);
            Assert.Null(compositionError);
            Assert.Same(changedDocument, catalog.FindIdentity(changed)!.Document);
            Assert.Same(document, catalog.FindIdentity(interactive)!.Document);
            Assert.Equal(interactiveIdentity.QualityRevision, catalog.FindIdentity(interactive)!.QualityRevision);
        }
    }
}
