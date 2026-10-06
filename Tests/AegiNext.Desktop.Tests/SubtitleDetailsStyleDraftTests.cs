using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleDetailsStyleDraftTests
{
    [Fact]
    public void PreciseShadowLoadAndSingleAxisRestoreRetainTheOtherPendingAxis()
    {
        var original = new SubtitleStyle { ShadowOffset = new(-1.1234567891234567, 7.987654321987654) };
        var draft = new SubtitleDetailsStyleDraft();
        draft.Load(original);
        Assert.Equal(original.ShadowOffset.X, double.Parse(draft.ShadowXText, CultureInfo.InvariantCulture));
        Assert.Equal(original.ShadowOffset.Y, double.Parse(draft.ShadowYText, CultureInfo.InvariantCulture));
        Assert.False(draft.IsDirty);
        Assert.False(draft.Read(original).HasOverrides);

        draft.ShadowXText = "-";
        draft.ShadowYText = "8e-";
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal(nameof(SubtitleDetailsStyleDraft.ShadowXText), draft.InvalidField);
        draft.RestoreField(nameof(SubtitleDetailsStyleDraft.ShadowXText));
        Assert.Null(draft.InvalidField);
        Assert.Equal(original.ShadowOffset.X, double.Parse(draft.ShadowXText, CultureInfo.InvariantCulture));
        Assert.Equal("8e-", draft.ShadowYText);
        Assert.True(draft.IsDirty);
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal(nameof(SubtitleDetailsStyleDraft.ShadowYText), draft.InvalidField);

        draft.ShadowYText = "8.125";
        var changed = draft.Read(original);
        Assert.Null(draft.InvalidField);
        Assert.Equal(new ScenePoint(original.ShadowOffset.X, 8.125), changed.ShadowOffset);
        Assert.Null(changed.FontFamily);
        Assert.Null(changed.FontSize);
    }

    [Theory]
    [InlineData(nameof(SubtitleDetailsStyleDraft.FontFamily))]
    [InlineData(nameof(SubtitleDetailsStyleDraft.FontSizeText))]
    public void InvalidFontRestoreKeepsPendingOutlineAndExactFill(string field)
    {
        var original = new SubtitleStyle { FontFamily = "Body font", FontAssetId = Guid.NewGuid(), FontSize = 42.125 };
        var fill = new SceneColor(4.123456789123456, -0.1234567891234567, 0.3456789012345678, 0.7312345678901234);
        var draft = new SubtitleDetailsStyleDraft();
        draft.Load(original);
        if (field == nameof(SubtitleDetailsStyleDraft.FontFamily))
        {
            draft.FontFamily = "  ";
        }
        else
        {
            draft.FontSizeText = "7e-";
        }
        draft.StrokeWidthText = "3.5";
        draft.Fill.SetValue(fill);
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal(field, draft.InvalidField);

        draft.RestoreField(field);
        Assert.Null(draft.InvalidField);
        Assert.Equal(original.FontFamily, draft.FontFamily);
        Assert.Equal(original.FontSize, double.Parse(draft.FontSizeText, CultureInfo.InvariantCulture));
        Assert.Equal("3.5", draft.StrokeWidthText);
        Assert.True(draft.IsDirty);
        var changed = draft.Read(original);
        Assert.Null(changed.FontFamily);
        Assert.Null(changed.FontSize);
        Assert.False(changed.ClearFontAsset);
        Assert.Equal(3.5, changed.StrokeWidth);
        Assert.Equal(fill, changed.Fill);
        Assert.Equal(original.FontAssetId, changed.ApplyTo(original).FontAssetId);
    }

    [Fact]
    public void RestoringColorRecoversOriginalHdrComponentsWhileKeepingNumericDraft()
    {
        var original = new SubtitleStyle
        {
            Fill = new(2.5123456789012345, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234)
        };
        var draft = new SubtitleDetailsStyleDraft();
        draft.Load(original);
        draft.Fill.HexText = "#20408080";
        draft.Fill.Red.RawText = "7e-";
        draft.StrokeWidthText = "3.25";
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal("Fill.Red", draft.InvalidField);

        draft.RestoreField(nameof(SubtitleDetailsStyleDraft.Fill));
        Assert.Null(draft.InvalidField);
        Assert.True(draft.Fill.TryCommit(out var restored));
        Assert.Equal(original.Fill, restored);
        Assert.Equal(original.Fill, draft.Fill.Value);
        Assert.False(draft.Fill.IsDirty);
        Assert.Null(draft.Fill.Error);
        Assert.True(draft.IsDirty);
        Assert.Equal("3.25", draft.StrokeWidthText);
        var changed = draft.Read(original);
        Assert.Null(changed.Fill);
        Assert.Equal(3.25, changed.StrokeWidth);
        Assert.Equal(original.Fill, changed.ApplyTo(original).Fill);
    }

    [Fact]
    public void LoadingAnotherStyleClearsFailureMetadataAndUnfinishedFields()
    {
        var draft = new SubtitleDetailsStyleDraft();
        draft.Load(new());
        draft.FontFamily = "  ";
        Assert.Throws<InvalidDataException>(() => draft.Read(new()));
        Assert.Equal(nameof(SubtitleDetailsStyleDraft.FontFamily), draft.InvalidField);
        var next = new SubtitleStyle { FontFamily = "Other font", FontSize = 72 };
        draft.Load(next);
        Assert.Null(draft.InvalidField);
        Assert.False(draft.IsDirty);
        Assert.False(draft.Read(next).HasOverrides);
    }
}
