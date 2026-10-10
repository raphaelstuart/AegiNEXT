using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleColorTagTests
{
    [Fact]
    public void EmptyProjectAndUntaggedLinesRemainValid()
    {
        ProjectValidator.Validate(new());
        Assert.Empty(new ProjectDocument().ColorTags);
        Assert.Null(new SubtitleLine().ColorTagId);
    }

    [Theory]
    [InlineData("", "#112233")]
    [InlineData(" ", "#112233")]
    [InlineData("bad\nname", "#112233")]
    [InlineData("tag", "#123")]
    [InlineData("tag", "#12345678")]
    [InlineData("tag", "#GG2233")]
    [InlineData("tag", "112233")]
    public void InvalidNamesAndColorsAreRejected(string name, string color)
    {
        var tag = new SubtitleColorTag { Name = name, ColorHex = color };
        Assert.Throws<InvalidDataException>(() => SubtitleColorTagValidator.Validate(tag));
    }

    [Fact]
    public void DuplicateAndEmptyIdentitiesAndDanglingAssignmentsAreRejected()
    {
        var tag = new SubtitleColorTag { Name = "Review", ColorHex = "#aaBBcc" };
        ProjectValidator.Validate(new() { ColorTags = [tag] });
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { ColorTags = [tag, tag] }));
        Assert.Throws<InvalidDataException>(() => SubtitleColorTagValidator.Validate(tag with { Id = Guid.Empty }));
        var line = new SubtitleLine { ColorTagId = tag.Id };
        var layer = new ProjectLayer { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
        ProjectValidator.Validate(document with { ColorTags = [tag] });
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with
        {
            ColorTags = [tag], Subtitles = [line with { ColorTagId = Guid.Empty }]
        }));
    }
}
