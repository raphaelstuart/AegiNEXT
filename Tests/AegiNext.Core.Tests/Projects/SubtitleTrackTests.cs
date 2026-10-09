using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleTrackTests
{
    [Fact]
    public void EmptyTrackCollectionIsValidWithoutSubtitlesButMissingCollectionIsRejected()
    {
        var document = new ProjectDocument { Tracks = [] };
        ProjectValidator.Validate(document);
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with { Tracks = default }));
    }

    [Fact]
    public void HalfOpenTouchingClipsAreValidButAnyOverlapOnTheSameTrackIsRejected()
    {
        var first = new SubtitleLine { Start = new(0), End = new(2), Text = "one" };
        var second = new SubtitleLine { Start = new(2), End = new(3), Text = "two" };
        var document = Document(first, second);
        ProjectValidator.Validate(document);

        var overlapping = second with { Start = new(1999, 1000) };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(first, overlapping)));
        var track = new ProjectTrack { Name = "Second" };
        var differentTracks = Document(first, overlapping);
        ProjectValidator.Validate(differentTracks with
        {
            Tracks = [ProjectTrack.Default, track],
            Layers = differentTracks.Layers.SetItem(1, differentTracks.Layers[1] with { TrackId = track.Id })
        });
    }

    [Fact]
    public void TracksAndCueReferencesCannotBeMissingDuplicateOrUnnamed()
    {
        var line = new SubtitleLine { Text = "valid" };
        var document = Document(line);
        ProjectValidator.Validate(document);

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with { Tracks = [] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with { Tracks = default }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with
        {
            Tracks = [ProjectTrack.Default, ProjectTrack.Default]
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with
        {
            Tracks = [ProjectTrack.Default with { Id = Guid.Empty }]
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with
        {
            Tracks = [ProjectTrack.Default with { Name = " " }]
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with
        {
            Layers = [document.Layers[0] with { TrackId = Guid.NewGuid() }]
        }));
    }

    private static ProjectDocument Document(params SubtitleLine[] lines)
    {
        return new()
        {
            Subtitles = [.. lines],
            Layers = [.. lines.Select(line => new ProjectLayer
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            })]
        };
    }
}
