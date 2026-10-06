using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests;

public sealed class AssTextLanguageTests
{
    [Theory]
    [InlineData("")]
    [InlineData("中文 ABC123 😀\\N第二行")]
    [InlineData("{\\an2\\fs48\\1c&H00FF00&}字幕")]
    [InlineData("{\\t(0,300,\\fs48\\alpha&H80&)}正文")]
    [InlineData("\\{literal\\} {\\unknown(1,2)\\fs")]
    [InlineData("{\\")]
    [InlineData("{\\fnNoto Sans CJK 123\\rStyle 123}")]
    [InlineData("{comment\r\n\\pos(-10.5,20)}\r\n正文")]
    public void TokensPartitionEveryUtf16CharacterIncludingIncompleteDrafts(string source)
    {
        var tokens = AssTextLanguage.Tokenize(source);
        var offset = 0;
        foreach (var token in tokens)
        {
            Assert.Equal(offset, token.Start);
            Assert.True(token.Length > 0);
            offset += token.Length;
        }

        Assert.Equal(source.Length, offset);
        Assert.Equal(source, string.Concat(tokens.Select(token => source.Substring(token.Start, token.Length))));
    }

    [Fact]
    public void AdjacentTagsSeparateNamesNumbersColorsAndStringParameters()
    {
        const string SOURCE = "{\\an2\\fs48\\1c&H00FF00&\\alpha&H80&\\fnNoto Sans 123\\rStyle 456}ABC123";
        var tokens = AssTextLanguage.Tokenize(SOURCE);
        AssertToken("\\an", SyntaxTokenKind.KEYWORD);
        AssertToken("\\fs", SyntaxTokenKind.KEYWORD);
        AssertToken("\\1c", SyntaxTokenKind.KEYWORD);
        AssertToken("48", SyntaxTokenKind.NUMBER);
        AssertToken("&H00FF00&", SyntaxTokenKind.STRING);
        AssertToken("Noto Sans 123", SyntaxTokenKind.STRING);
        AssertToken("Style 456", SyntaxTokenKind.STRING);
        AssertToken("ABC123", SyntaxTokenKind.TEXT);

        void AssertToken(string text, SyntaxTokenKind kind)
        {
            Assert.Equal(kind, Assert.Single(tokens, token => SOURCE.Substring(token.Start, token.Length) == text).Kind);
        }
    }

    [Fact]
    public void EscapedBracesDoNotStartAnOverrideBlockAndNestedTagsRemainHighlighted()
    {
        const string SOURCE = "\\{\\fs48\\}\\N{\\t(0,300,\\fs24)}";
        var tokens = AssTextLanguage.Tokenize(SOURCE);
        Assert.Equal(1, tokens.Count(token => SOURCE.Substring(token.Start, token.Length) == "\\fs"));
        foreach (var escape in new[] { "\\{", "\\}", "\\N" })
        {
            Assert.Equal(SyntaxTokenKind.FUNCTION, Assert.Single(tokens,
                token => SOURCE.Substring(token.Start, token.Length) == escape).Kind);
        }

        Assert.Contains(tokens, token => SOURCE.Substring(token.Start, token.Length) == "24" && token.Kind == SyntaxTokenKind.NUMBER);
    }
}
