using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptGroupingTests
{
    [Theory]
    [InlineData("A😀e\u0301👩‍💻🇨🇳", new[] { "A", "😀", "e\u0301", "👩‍💻", "🇨🇳" })]
    [InlineData(" A\tB\u00a0C\r\nD ", new[] { "A", "B", "C", "D" })]
    public void GraphemeGroupingKeepsUnicodeElementsAndSkipsWhitespace(string text, string[] expected)
    {
        AssertGroups(text, Scope(EffectScriptUnitKind.GRAPHEME), expected);
    }

    [Theory]
    [InlineData("ABCDE", 2, new[] { "AB", "CD", "E" })]
    [InlineData("A B C", 2, new[] { "A ", "B ", "C" })]
    [InlineData("AB   C", 2, new[] { "AB", " C" })]
    [InlineData("A\nBCD\r\nEFG", 2, new[] { "A", "BC", "D", "EF", "G" })]
    [InlineData("😀e\u0301👩‍💻X", 2, new[] { "😀e\u0301", "👩‍💻X" })]
    [InlineData("AB", int.MaxValue, new[] { "AB" })]
    public void ChunkGroupingCountsLineWhitespaceRestartsAtHardLinesAndKeepsItsTail(string text, int count, string[] expected)
    {
        AssertGroups(text, Scope(EffectScriptUnitKind.CHUNK, count), expected);
    }

    [Theory]
    [InlineData("Hello, foo,bar! -- 中文连续文本", new[] { "Hello,", "foo,bar!", "--", "中文连续文本" })]
    [InlineData(" e\u0301\t👩‍💻\r\n😀 ", new[] { "e\u0301", "👩‍💻", "😀" })]
    public void WordGroupingPreservesPunctuationInsideWhitespaceSeparatedTokens(string text, string[] expected)
    {
        AssertGroups(text, Scope(EffectScriptUnitKind.WORD), expected);
    }

    [Theory]
    [InlineData(" A \r\n\r\n\t\nB\n C ", new[] { " A ", "B", " C " })]
    [InlineData("ABC", new[] { "ABC" })]
    [InlineData("\nA\n", new[] { "A" })]
    public void LineGroupingKeepsLineContentsAndExcludesHardBreaksAndBlankLines(string text, string[] expected)
    {
        AssertGroups(text, Scope(EffectScriptUnitKind.LINE), expected);
    }

    [Theory]
    [InlineData("A\nB\n\nC", new[] { "A\nB", "C" })]
    [InlineData(" A \r\nB \r\n \t\r\n C \n\n", new[] { " A \r\nB ", " C " })]
    [InlineData("\n\t\nA\n", new[] { "A" })]
    public void ParagraphGroupingPreservesInternalBreaksAndSeparatesAtBlankLines(string text, string[] expected)
    {
        AssertGroups(text, Scope(EffectScriptUnitKind.PARAGRAPH), expected);
    }

    [Theory]
    [InlineData("| A || B |", "|", new[] { "A", "B" })]
    [InlineData("A,B", "|", new[] { "A,B" })]
    [InlineData(" A \r\n B ", "\r\n", new[] { "A", "B" })]
    [InlineData("A\nB| C ", "|", new[] { "A\nB", "C" })]
    [InlineData("😀👩‍💻e\u0301", "👩‍💻", new[] { "😀", "e\u0301" })]
    public void LiteralSplitKeepsSeparatorsOutsideGroupsAndTrimsOnlyHorizontalWhitespace(string text, string delimiter, string[] expected)
    {
        AssertGroups(text, Scope(EffectScriptUnitKind.SPLIT, delimiters: [delimiter]), expected);
    }

    [Fact]
    public void LiteralSplitChoosesTheLongestMatchingSeparatorIndependentlyOfDeclarationOrder()
    {
        var scope = Scope(EffectScriptUnitKind.SPLIT, delimiters: [":", "::"]);
        var expectedStarts = new[] { 0, 3, 5 };

        var groups = AssertGroups("A::B:C", scope, "A", "B", "C");

        Assert.Equal(expectedStarts, groups.Select(group => group.Utf16Start));
    }

    [Theory]
    [InlineData("e\u0301X", "e")]
    [InlineData("e\u0301X", "\u0301")]
    [InlineData("👩‍💻X", "👩")]
    [InlineData("👩‍💻X", "💻")]
    [InlineData("A\r\nB", "\n")]
    public void LiteralSplitRejectsMatchesThatCutCompleteGraphemes(string text, string delimiter)
    {
        var scope = Scope(EffectScriptUnitKind.SPLIT, delimiters: [delimiter]);

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptGroupResolver.Resolve(scope, new() { Text = text }));

        Assert.Equal(scope.Line, error.Line);
        Assert.Equal(scope.Column, error.Column);
        Assert.Contains("字素", error.Message);
    }

    [Fact]
    public void LiteralSplitDoesNotTrimAWhitespaceCharacterAwayFromItsCombiningMark()
    {
        AssertGroups(" \u0301A|B", Scope(EffectScriptUnitKind.SPLIT, delimiters: ["|"]), " \u0301A", "B");
    }

    [Fact]
    public void LiteralSplitTrimsUnicodeHorizontalWhitespaceWithoutRemovingVerticalWhitespace()
    {
        AssertGroups("\t\u00a0 A \u2003|\nB\n|\u2028C\u2028", Scope(EffectScriptUnitKind.SPLIT, delimiters: ["|"]),
            "A", "\nB\n", "\u2028C\u2028");
    }

    [Fact]
    public void ExplicitRangeUsesOneBasedWholeSubtitleGraphemesAndNeverExpandsTokens()
    {
        var scope = Scope(EffectScriptUnitKind.WORD) with { Target = new(EffectScriptTargetKind.RANGE, 2, 2) };

        var groups = AssertGroups("😀ABCD", scope, "AB");

        Assert.Equal(new EffectScriptTextGroup(2, 2), Assert.Single(groups));
    }

    [Fact]
    public void CurrentRangeRestrictsGroupingAndSubtitleTargetIgnoresThatRange()
    {
        const string TEXT = "a😀e\u0301b";
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 4);
        var subtitle = new SubtitleLine { Text = TEXT, AnimationRanges = [range] };
        var context = new AnimationTrackTarget(AnimationProperty.SCALE, TextRangeId: range.Id);
        var current = Scope(EffectScriptUnitKind.GRAPHEME);

        Assert.Equal(new[] { new EffectScriptTextGroup(1, 2), new EffectScriptTextGroup(3, 2) },
            EffectScriptGroupResolver.Resolve(current, subtitle, context));
        Assert.Equal(4, EffectScriptGroupResolver.Resolve(current with { Target = new(EffectScriptTargetKind.SUBTITLE) }, subtitle, context).Length);
    }

    [Fact]
    public void CurrentTargetWithoutRangeUsesTheWholeSubtitleAndReverseOnlyChangesOrder()
    {
        var scope = Scope(EffectScriptUnitKind.CHUNK, 2) with { Order = EffectScriptOrder.REVERSE };
        var expectedStarts = new[] { 4, 2, 0 };

        var groups = AssertGroups("ABCDE", scope, "E", "CD", "AB");

        Assert.Equal(expectedStarts, groups.Select(group => group.Utf16Start));
    }

    [Fact]
    public void SplitDoesNotConsumeADelimiterOutsideTheSelectedSourceRange()
    {
        var scope = Scope(EffectScriptUnitKind.SPLIT, delimiters: ["::"]) with
        {
            Target = new(EffectScriptTargetKind.RANGE, 1, 2)
        };

        AssertGroups("A::B", scope, "A:");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(4, 1)]
    [InlineData(2, 3)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void ExplicitRangeRejectsInvalidOrOutOfBoundsGraphemeIndices(int start, int count)
    {
        var scope = Scope() with { Target = new(EffectScriptTargetKind.RANGE, start, count) };

        AssertLocatedError(scope, new() { Text = "ABC" });
    }

    [Fact]
    public void CurrentTargetRejectsMissingRangeIdentityInsteadOfUsingTheWholeText()
    {
        var scope = Scope();
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptGroupResolver.Resolve(scope,
            new() { Text = "ABC" }, new(AnimationProperty.SCALE, TextRangeId: Guid.NewGuid())));

        Assert.Equal(scope.Line, error.Line);
        Assert.Equal(scope.Column, error.Column);
    }

    [Fact]
    public void CurrentTargetRejectsARangeThatSplitsAGrapheme()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var scope = Scope();

        var error = Assert.Throws<EffectScriptException>(() => EffectScriptGroupResolver.Resolve(scope,
            new() { Text = "😀X", AnimationRanges = [range] }, new(AnimationProperty.SCALE, TextRangeId: range.Id)));

        Assert.Equal(scope.Line, error.Line);
        Assert.Contains("字素", error.Message);
    }

    [Theory]
    [InlineData(EffectScriptUnitKind.GROUP)]
    [InlineData(EffectScriptUnitKind.GRAPHEME)]
    [InlineData(EffectScriptUnitKind.CHUNK)]
    [InlineData(EffectScriptUnitKind.WORD)]
    [InlineData(EffectScriptUnitKind.LINE)]
    [InlineData(EffectScriptUnitKind.PARAGRAPH)]
    [InlineData(EffectScriptUnitKind.SPLIT)]
    public void EmptyAndWhitespaceOnlySourcesDoNotProduceInvalidOrInvisibleGroups(EffectScriptUnitKind kind)
    {
        var scope = Scope(kind, delimiters: ["|"]);

        Assert.Empty(EffectScriptGroupResolver.Resolve(scope, new() { Text = string.Empty }));
        Assert.Empty(EffectScriptGroupResolver.Resolve(scope, new() { Text = " \t\r\n\n" }));
    }

    [Fact]
    public void OutputBudgetAllows256GroupsAndRejectsTheNextBeforeCompilation()
    {
        var scope = Scope(EffectScriptUnitKind.GRAPHEME);

        Assert.Equal(256, EffectScriptGroupResolver.Resolve(scope, new() { Text = new('A', 256) }).Length);
        var error = AssertLocatedError(scope, new() { Text = new('A', 257) });

        Assert.Contains("256", error.Message);
    }

    [Fact]
    public void InvalidUnitConfigurationHasASourceLocation()
    {
        AssertLocatedError(Scope(EffectScriptUnitKind.CHUNK, 0), new() { Text = "ABC" });
        AssertLocatedError(Scope(EffectScriptUnitKind.SPLIT), new() { Text = "ABC" });
        AssertLocatedError(Scope(EffectScriptUnitKind.SPLIT, delimiters: [string.Empty]), new() { Text = "ABC" });
    }

    private static EffectScriptScope Scope(EffectScriptUnitKind kind = EffectScriptUnitKind.GROUP, int count = 1,
        params string[] delimiters)
    {
        return new("groups", new(EffectScriptTargetKind.CURRENT), [], 11, 5)
        {
            Unit = new(kind) { Count = count, Delimiters = delimiters.ToImmutableArray() }
        };
    }

    private static ImmutableArray<EffectScriptTextGroup> AssertGroups(string text, EffectScriptScope scope, params string[] expected)
    {
        var groups = EffectScriptGroupResolver.Resolve(scope, new() { Text = text });
        var boundaries = new SubtitleTextBoundaries(text);

        Assert.Equal(expected, groups.Select(group => text.Substring(group.Utf16Start, group.Utf16Length)));
        Assert.All(groups, group =>
        {
            Assert.True(group.Utf16Length > 0);
            Assert.InRange(group.Utf16Start, 0, text.Length - 1);
            Assert.InRange(group.Utf16Start + group.Utf16Length, 1, text.Length);
            Assert.True(boundaries.Contains(group.Utf16Start));
            Assert.True(boundaries.Contains(group.Utf16Start + group.Utf16Length));
        });
        return groups;
    }

    private static EffectScriptException AssertLocatedError(EffectScriptScope scope, SubtitleLine subtitle)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptGroupResolver.Resolve(scope, subtitle));

        Assert.Equal(scope.Line, error.Line);
        Assert.Equal(scope.Column, error.Column);
        return error;
    }
}
