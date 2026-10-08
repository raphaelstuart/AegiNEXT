using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTextSizeTests
{
    [Fact]
    public void PlainDocumentNormalizationDoesNotAllocatePerTextCharacter()
    {
        var document = Document(new string('字', 1_048_577));
        SubtitleKaraokeNormalization.Normalize(document);
        SubtitleKaraokeNormalization.Normalize(document);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var normalized = SubtitleKaraokeNormalization.Normalize(document);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(document, normalized);
        Assert.True(allocated < 64 * 1024, $"Plain document normalization allocated {allocated} bytes.");
    }

    [Fact]
    public void PlainLineNormalizationStillValidatesUnicodeAndSavedHighlightStyle()
    {
        var line = new SubtitleLine { Text = new string('字', 1_000_001) };

        Assert.Same(line, SubtitleKaraokeNormalization.Normalize(line));
        Assert.Throws<InvalidDataException>(() => SubtitleKaraokeNormalization.Normalize(line with { Text = line.Text + "\uD800" }));
        Assert.Throws<InvalidDataException>(() => SubtitleKaraokeNormalization.Normalize(line with
        {
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.Empty, "Saved", new())
        }));
        Assert.Throws<InvalidDataException>(() => SubtitleKaraokeNormalization.Normalize(line with { InactiveKaraoke = default }));
    }

    [Fact]
    public void LargeTextCanBeEditedAndStillUsesCompleteGraphemeBoundaries()
    {
        var document = Document(new string('字', 1_000_001) + "e");
        var line = document.Subtitles[0];

        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, line.Text.Length, 0, "\u0301😀");

        Assert.Equal(line.Text + "\u0301😀", edited.Subtitles[0].Text);
        Assert.Empty(edited.Subtitles[0].Karaoke);
        Assert.Equal(document.Layers, edited.Layers);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectEditingOperations.ReplaceSubtitleTextRange(
            edited, line.Id, line.Text.Length, 0, "x"));
    }

    [Fact]
    public void LargePlainTextReplacementAllocatesTextAndBoundaryStorageWithoutPerCharacterObjects()
    {
        var document = Document(new string('字', 1_048_577));
        var line = document.Subtitles[0];
        var replacement = new string('x', line.Text.Length);
        ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 0, line.Text.Length, replacement);
        ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 0, line.Text.Length, replacement);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 0, line.Text.Length, replacement);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var storage = ((long)line.Text.Length + replacement.Length) * sizeof(int) +
            (long)replacement.Length * sizeof(char);
        Assert.Equal(replacement, edited.Subtitles[0].Text);
        Assert.True(line.InlineSpans == edited.Subtitles[0].InlineSpans);
        Assert.True(allocated < storage + 4 * 1024 * 1024,
            $"Plain text replacement allocated {allocated} bytes for {storage} bytes of text and boundary storage.");
    }

    [Fact]
    public void AssRoundTripsASingleSubtitleLargerThanOneMillionCharacters()
    {
        var document = Document(new string('字', 1_000_001));

        var source = AssSubtitleFormat.Write(document).Text;
        var imported = AssSubtitleFormat.Parse(source);

        Assert.Equal(document.Subtitles[0].Text, Assert.Single(imported.Lines).Text);
    }

    private static ProjectDocument Document(string text)
    {
        var line = new SubtitleLine { Text = text };
        return new()
        {
            Subtitles = [line],
            Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }]
        };
    }
}
