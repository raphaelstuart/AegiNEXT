using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ProjectTextValidationTests
{
    [Fact]
    public void LargeSubtitleLinesAndTotalTextRemainValid()
    {
        var text = new string('字', 1_048_577);
        var document = Document(text, 9);

        ProjectValidator.Validate(document);
        ProjectValidator.ValidateSubtitleKaraoke(document.Subtitles[0]);

        Assert.True(document.Subtitles.Sum(line => (long)line.Text.Length) > 8 * 1024 * 1024);
        Assert.Same(text, document.Subtitles[0].Text);
    }

    [Fact]
    public void LargePlainSubtitleValidationDoesNotAllocatePerCharacter()
    {
        var document = Document(new string('字', 1_048_577));
        ProjectValidator.Validate(document);
        ProjectValidator.Validate(document);
        var before = GC.GetAllocatedBytesForCurrentThread();

        ProjectValidator.Validate(document);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64 * 1024, $"Plain subtitle validation allocated {allocated} bytes.");
    }

    [Fact]
    public void SharedIndexMustBelongToTheValidatedSubtitle()
    {
        var line = new SubtitleLine { Text = "😀e\u0301" };

        ProjectValidator.ValidateSubtitleKaraoke(line, new(line.Text));

        Assert.Throws<ArgumentException>(() => ProjectValidator.ValidateSubtitleKaraoke(line, new("other text")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LargeTextStillRejectsRangesThatSplitAGrapheme(int kind)
    {
        var text = new string('字', 1_000_001) + "😀e\u0301";
        var document = Document(text);
        var line = document.Subtitles[0];
        var invalid = kind switch
        {
            0 => line with { InlineSpans = [new(1_000_001, 1, new() { Bold = true })] },
            1 => line with { Karaoke = [new(1_000_001, 1, new(0), new(1), SceneColor.White)] },
            _ => line with { InactiveKaraoke = [new(1_000_003, 1, new(0), new(1), SceneColor.White)] }
        };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with { Subtitles = [invalid] }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("普通文字\t\n")]
    [InlineData("😀e\u0301👩‍💻")]
    [InlineData("\uD800\uDC00\uDBFF\uDFFF")]
    public void UnicodeValidationPreservesValidScalarSequences(string text)
    {
        ProjectValidator.ValidateText(text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void UnicodeValidationRejectsNullAndUnpairedSurrogates(int kind)
    {
        var text = kind switch
        {
            0 => "\0",
            1 => "\uD800",
            2 => "\uDC00",
            3 => "\uD800x",
            4 => "\uD800\uD800\uDC00",
            5 => "\uD800\uDC00\0",
            _ => "\uD800\uDC00\uDC00"
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateText(text));
    }

    private static ProjectDocument Document(string text, int count = 1)
    {
        var lines = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Text = text, Start = new(index), End = new(index + 1)
        }).ToImmutableArray();
        return new()
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
    }
}
