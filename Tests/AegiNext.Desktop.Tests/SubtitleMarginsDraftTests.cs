using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleMarginsDraftTests
{
    [Fact]
    public void LoadingMarginsPreservesUnequalEdgesAndDoublePrecisionWithoutChanges()
    {
        var margins = new SubtitleMargins(0.12500000000000003, double.Epsilon, 32768);
        var draft = new SubtitleMarginsDraft();
        var changes = 0;
        draft.Changed += (_, _) => changes++;

        draft.Load(margins);

        Assert.Null(draft.Validate());
        Assert.Equal(margins, draft.CreateMargins());
        Assert.Equal(margins, draft.DiagramMargins);
        Assert.False(draft.IsDirty);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData("", "MarginLeftInput")]
    [InlineData("7e-", "MarginLeftInput")]
    [InlineData("NaN", "MarginLeftInput")]
    [InlineData("Infinity", "MarginLeftInput")]
    [InlineData("-0.1", "MarginLeftInput")]
    [InlineData("32768.1", "MarginLeftInput")]
    public void InvalidRawTextRemainsEditableAndCannotBecomeValidDiagram(string raw, string expected)
    {
        var draft = new SubtitleMarginsDraft();
        draft.Load(new(17, 39, 13));
        draft.Left.RawText = raw;

        Assert.Equal(raw, draft.Left.RawText);
        Assert.Equal(expected, draft.Validate());
        Assert.Null(draft.DiagramMargins);
        Assert.True(draft.IsDirty);
        Assert.Throws<InvalidDataException>(() => draft.CreateMargins());
    }

    [Fact]
    public void ValidationReportsTheFirstInvalidEdgeAndAcceptsZeroAndMaximum()
    {
        var draft = new SubtitleMarginsDraft();
        draft.Load(new(0, 32768, 0));
        Assert.Null(draft.Validate());
        draft.Right.RawText = "7e-";
        draft.Vertical.RawText = "32769";
        Assert.Equal("MarginRightInput", draft.Validate());
        draft.Right.RawText = "32768";
        Assert.Equal("MarginVerticalInput", draft.Validate());
        draft.Vertical.RawText = "0";
        Assert.Null(draft.Validate());
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void RawChangesNotifyOnceIncludingInvalidAndEquivalentRepresentations()
    {
        var draft = new SubtitleMarginsDraft();
        var changes = 0;
        draft.Changed += (_, _) => changes++;
        draft.Left.RawText = "7e-";
        Assert.Equal(1, changes);
        draft.Left.RawText = "17";
        Assert.Equal(2, changes);
        draft.Left.RawText = "17";
        Assert.Equal(2, changes);
        draft.Left.RawText = "40.0";
        Assert.Equal(3, changes);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void RestoreAndLoadFieldChangeOnlyTheRequestedEdgeAndItsSourceSnapshot()
    {
        var precise = 0.12500000000000003;
        var draft = new SubtitleMarginsDraft();
        draft.Load(new(precise, 39, 13));
        draft.Left.RawText = "7e-";
        draft.Right.RawText = "7e-";
        draft.Vertical.RawText = "17";
        var changes = 0;
        draft.Changed += (_, _) => changes++;

        Assert.True(draft.RestoreField("MarginLeftInput"));
        Assert.Equal(1, changes);
        Assert.Equal("7e-", draft.Right.RawText);
        Assert.Equal("17", draft.Vertical.RawText);
        Assert.Equal("MarginRightInput", draft.Validate());
        Assert.True(draft.LoadField("MarginRightInput", 23));
        Assert.Equal(2, changes);
        Assert.Equal(new SubtitleMargins(precise, 23, 17), draft.CreateMargins());
        draft.Right.RawText = "42";
        Assert.True(draft.RestoreField("MarginRightInput"));
        Assert.Equal(23, draft.CreateMargins().Right);
        Assert.False(draft.RestoreField("MissingInput"));
        Assert.False(draft.LoadField("MissingInput", 0));
    }

    [Fact]
    public void ExplicitLoadCultureSupportsFractionalMarginsWithoutRounding()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var draft = new SubtitleMarginsDraft();

        Assert.True(draft.LoadField("MarginLeftInput", 0.12500000000000003, culture));

        Assert.Contains(',', draft.Left.RawText);
        Assert.Equal(0.12500000000000003, draft.CreateMargins().Left);
        draft.Left.RawText = "17,5";
        Assert.Equal(17.5, draft.CreateMargins().Left);
        Assert.True(draft.IsDirty);
    }
}
