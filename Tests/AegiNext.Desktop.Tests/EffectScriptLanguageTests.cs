using AegiNext.Desktop.Controls;
using System.Globalization;

namespace AegiNext.Desktop.Tests;

public sealed class EffectScriptLanguageTests
{
    private static readonly string[] absolutePathValues = ["0", "1"];
    private static readonly string[] colorValues = ["base", "rgba(1, 1, 1, 1)"];

    [Fact]
    public void TokensPartitionSourceAndGiveDistinctSyntaxCategories()
    {
        const string SOURCE = "effect \"personal\" version 1\n# comment\nsegment enter fixed 300ms\n at 0 position offset(-20, 0) ease-out\nend";
        var tokens = EffectScriptLanguage.Tokenize(SOURCE);
        Assert.Equal(SOURCE, string.Concat(tokens.Select(token => SOURCE.Substring(token.Start, token.Length))));
        foreach (var expected in new[] { SyntaxTokenKind.KEYWORD, SyntaxTokenKind.PROPERTY, SyntaxTokenKind.NUMBER,
                     SyntaxTokenKind.STRING, SyntaxTokenKind.COMMENT, SyntaxTokenKind.FUNCTION, SyntaxTokenKind.INTERPOLATION })
        {
            Assert.Contains(tokens, token => token.Kind == expected);
        }
    }

    [Theory]
    [InlineData("", "effect \"my-effect\" version 1")]
    [InlineData("effect \"test\" version 1\n", "short-clip compress")]
    [InlineData("effect \"test\" version 1\nshort-clip compress\n", "segment stay flex 1")]
    [InlineData("effect \"test\" version 1\nshort-clip compress\nsegment enter fixed 300ms\n    at 0 po", "position")]
    [InlineData("at 0 position ", "offset(0, 0)")]
    [InlineData("at 0 opacity ", "offset(0)")]
    [InlineData("at 0 letter-s", "letter-spacing")]
    [InlineData("at 0 fill-b", "fill-blur")]
    [InlineData("at 0 stroke-b", "stroke-blur")]
    [InlineData("at 0 letter-spacing ", "offset(0)")]
    [InlineData("at 0 fill-blur ", "factor(1)")]
    [InlineData("at 0 stroke-blur ", "base")]
    [InlineData("at 0 position offset(10, 20) ea", "ease-out")]
    [InlineData("at 0 fi", "fill")]
    [InlineData("at\t0\tpo", "position")]
    [InlineData("at\t0\tfill\t", "rgba(1, 1, 1, 1)")]
    [InlineData("at 0 stroke ", "rgba(1, 1, 1, 1)")]
    [InlineData("at 0 fill ", "base")]
    [InlineData("at 0 fill rgba(2.5, 0, 0, 0.5) ea", "ease-out")]
    [InlineData("at 0 mask-po", "mask-position")]
    [InlineData("at 0 mask-node", "mask-node(1,1).position")]
    [InlineData("at 0 mask-node(2,5).po", "mask-node(2,5).position")]
    [InlineData("at 0 mask-node(2, 5).in", "mask-node(2, 5).in-handle")]
    [InlineData("at 0 mask-node(1,1).position ", "offset(0, 0)")]
    [InlineData("at 0 mask-node(1, 2).out-handle ", "(0, 0)")]
    [InlineData("at 0 mask-scale ", "factor(1, 1)")]
    [InlineData("at 0 mask-rotation ", "offset(0)")]
    [InlineData("at 0 mask-position base po", "power(2)")]
    public void CompletionUsesTheCurrentGrammarAndReplacesOnlyThePartialWord(string source, string insertion)
    {
        var item = Assert.Single(EffectScriptLanguage.Complete(source, source.Length), value => value.Insertion == insertion);
        var result = source.Remove(item.Start, item.Length).Insert(item.Start, item.Insertion);
        Assert.EndsWith(insertion, result, StringComparison.Ordinal);
        Assert.Equal(source[..item.Start], result[..item.Start]);
    }

    [Theory]
    [InlineData("# at 0 po")]
    [InlineData("effect \"incomplete")]
    [InlineData("at 0 position offset(")]
    public void CommentsStringsAndIncompleteFunctionArgumentsDoNotOfferWrongGrammar(string source)
    {
        Assert.Empty(EffectScriptLanguage.Complete(source, source.Length));
    }

    [Fact]
    public void PathProgressNeverOffersUndefinedRelativeBaseValues()
    {
        const string SOURCE = "at 0 path-progress ";
        var completions = EffectScriptLanguage.Complete(SOURCE, SOURCE.Length);
        Assert.Equal(absolutePathValues, completions.Select(item => item.Insertion));
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("stroke")]
    public void ColorCompletionOnlyOffersSupportedRgbaAndBaseValues(string property)
    {
        var source = $"at 0 {property} ";
        var completions = EffectScriptLanguage.Complete(source, source.Length);
        Assert.Equal(colorValues, completions.Select(item => item.Insertion));
        Assert.DoesNotContain(completions, item => item.Insertion.StartsWith("offset", StringComparison.Ordinal) ||
            item.Insertion.StartsWith("factor", StringComparison.Ordinal));
        var syntax = $"at 0 {property} rgba(2.5, -0.1, 0.25, 0.5) ease-out";
        var tokens = EffectScriptLanguage.Tokenize(syntax);
        Assert.Equal(SyntaxTokenKind.PROPERTY, tokens.Single(token => syntax.Substring(token.Start, token.Length) == property).Kind);
        Assert.Equal(SyntaxTokenKind.FUNCTION, tokens.Single(token => syntax.Substring(token.Start, token.Length) == "rgba").Kind);
    }

    [Theory]
    [InlineData("letter-spacing")]
    [InlineData("fill-blur")]
    [InlineData("stroke-blur")]
    public void SubtitleAppearancePropertiesUseTheSharedMetadataForHighlighting(string property)
    {
        var source = $"at 0 {property} offset(2) linear";
        var token = Assert.Single(EffectScriptLanguage.Tokenize(source), value =>
            source.Substring(value.Start, value.Length) == property);
        Assert.Equal(SyntaxTokenKind.PROPERTY, token.Kind);
    }

    [Fact]
    public void NodeSelectorAndPowerHaveCompleteSyntaxCoverage()
    {
        const string SOURCE = "at 0 mask-node(2, 3).out-handle offset(5, -2) power(1.5)";
        var tokens = EffectScriptLanguage.Tokenize(SOURCE);

        Assert.Equal(SOURCE, string.Concat(tokens.Select(token => SOURCE.Substring(token.Start, token.Length))));
        Assert.Equal(SyntaxTokenKind.PROPERTY, tokens.Single(token => SOURCE.Substring(token.Start, token.Length) == "mask-node(2, 3).out-handle").Kind);
        Assert.Equal(SyntaxTokenKind.INTERPOLATION, tokens.Single(token => SOURCE.Substring(token.Start, token.Length) == "power").Kind);
    }
}
