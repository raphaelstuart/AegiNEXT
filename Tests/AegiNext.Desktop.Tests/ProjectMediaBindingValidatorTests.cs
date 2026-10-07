using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class ProjectMediaBindingValidatorTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1001, 30000)]
    [InlineData(-1001, 30000)]
    public void EqualExactOriginsAndSelectedStreamsAreAccepted(long numerator, long denominator)
    {
        var document = CreateDocument(new(numerator, denominator));
        var media = new VideoPreviewMedia(0, new(numerator * 2, denominator * 2), new(10), 1, 1920, 1080);
        ProjectMediaBindingValidator.Validate(document, media);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(0, null)]
    public void ChangedSelectedStreamCannotReuseTheSavedBinding(int video, int? audio)
    {
        Assert.Throws<InvalidDataException>(() => ProjectMediaBindingValidator.Validate(CreateDocument(new(0)),
            new(video, new(0), new(10), audio, 1920, 1080)));
    }

    [Fact]
    public void ExactOriginMismatchIsRejectedEvenWhenDisplayedMillisecondsAgree()
    {
        Assert.Throws<InvalidDataException>(() => ProjectMediaBindingValidator.Validate(CreateDocument(new(1, 3)),
            new(0, new(333, 1000), new(10), 1, 1920, 1080)));
    }

    [Fact]
    public void ConfirmedPlaybackOriginMismatchIsRejectedWhileUnknownMetadataRemainsUnknown()
    {
        var document = CreateDocument(new(3));
        document = document with { Media = document.Media! with { PlaybackOrigin = new(29, 10) } };
        var media = new VideoPreviewMedia(0, new(3), new(10), 1, 1920, 1080) { PlaybackOrigin = new(3) };
        Assert.Throws<InvalidDataException>(() => ProjectMediaBindingValidator.Validate(document, media));
        ProjectMediaBindingValidator.Validate(document, media with { PlaybackOrigin = null });
        ProjectMediaBindingValidator.Validate(document with { Media = document.Media with { PlaybackOrigin = null } }, media);
    }

    [Fact]
    public void UnknownStartHasExplicitZeroIdentityMappingOnly()
    {
        var unknown = new VideoPreviewMedia(0, null, null, 1, 1920, 1080);
        ProjectMediaBindingValidator.Validate(CreateDocument(new(0)), unknown);
        Assert.Throws<InvalidDataException>(() => ProjectMediaBindingValidator.Validate(CreateDocument(new(1)), unknown));
    }

    [Theory]
    [InlineData(null, 1080)]
    [InlineData(1920, null)]
    [InlineData(0, 1080)]
    [InlineData(1920, -1)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1088)]
    public void UnknownOrMismatchedCanvasDimensionsFailBeforePreviewCommit(int? width, int? height)
    {
        Assert.Throws<InvalidDataException>(() => ProjectMediaBindingValidator.Validate(CreateDocument(new(0)),
            new(0, new(0), new(10), 1, width, height)));
    }

    [Fact]
    public void AudioLessBindingIsAcceptedOnlyForAudioLessMedia()
    {
        var document = CreateDocument(new(0));
        document = document with { Media = document.Media! with { AudioStreamIndex = null } };
        ProjectMediaBindingValidator.Validate(document, new(0, new(0), null, null, 1920, 1080));
    }

    private static ProjectDocument CreateDocument(MediaTime origin)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "video.mkv");
        return new() { Assets = [asset], Media = new(asset.Id, 0, 1, origin) };
    }
}
