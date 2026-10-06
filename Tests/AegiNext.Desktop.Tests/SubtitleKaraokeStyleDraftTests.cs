using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleKaraokeStyleDraftTests
{
    [Fact]
    public void SeparateVisualDraftRetainsUneditedPrecisionAndSelectionStyle()
    {
        var original = new KaraokeHighlightStyle
        {
            PresetId = Guid.NewGuid(), PresetName = "HDR", Fill = new(4.123456789, 0.25, 1, 0.6),
            StrokeWidth = 0.12345678912345678, ShadowOffset = new(-1.1234567891234567, 7.25)
        };
        var highlight = new SubtitleKaraokeStyleDraft();
        var selection = new SubtitleDetailsStyleDraft();
        selection.Load(new() { FontFamily = "Body font", FontSize = 42 });
        highlight.Load(original);
        Assert.False(highlight.IsDirty);
        Assert.Equal(original, highlight.Read(original));
        highlight.ShadowYText = "8.125";
        var changed = highlight.Read(original);
        Assert.Equal(original.Fill, changed.Fill);
        Assert.Equal(original.StrokeWidth, changed.StrokeWidth);
        Assert.Equal(original.ShadowOffset.X, changed.ShadowOffset.X);
        Assert.Equal(8.125, changed.ShadowOffset.Y);
        Assert.Equal(original.PresetId, changed.PresetId);
        Assert.False(selection.IsDirty);
        Assert.Equal("Body font", selection.FontFamily);
        Assert.Equal("42", selection.FontSizeText);
    }

    [Fact]
    public void InvalidHighlightNumericRemainsDraftUntilExplicitRestore()
    {
        var original = new KaraokeHighlightStyle { PresetId = Guid.NewGuid(), PresetName = "Custom", StrokeWidth = 0.123456789 };
        var draft = new SubtitleKaraokeStyleDraft();
        draft.Load(original);
        draft.StrokeWidthText = "-";
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal("-", draft.StrokeWidthText);
        Assert.True(draft.IsDirty);
        draft.Load(original);
        Assert.False(draft.IsDirty);
        Assert.Equal(original, draft.Read(original));
    }

    [Fact]
    public void RestoringHighlightBlurKeepsPendingFillOffsetsAndBodyTypography()
    {
        var original = new KaraokeHighlightStyle
        {
            PresetId = Guid.NewGuid(), PresetName = "HDR", ShadowBlur = 0.12345678912345678,
            ShadowOffset = new(-1.1234567891234567, 7.987654321987654)
        };
        var bodyStyle = new SubtitleStyle { FontFamily = "Body font", FontSize = 42.125, Bold = true, Italic = true };
        var body = new SubtitleDetailsStyleDraft();
        body.Load(bodyStyle);
        var fill = new SceneColor(4.123456789123456, -0.1234567891234567, 0.3456789012345678, 0.7312345678901234);
        var draft = new SubtitleKaraokeStyleDraft();
        draft.Load(original);
        draft.Fill.SetValue(fill);
        draft.ShadowXText = "2.25";
        draft.ShadowYText = "8.125";
        draft.ShadowBlurText = "7e-";
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal(nameof(SubtitleKaraokeStyleDraft.ShadowBlurText), draft.InvalidField);

        draft.RestoreField(nameof(SubtitleKaraokeStyleDraft.ShadowBlurText));
        Assert.Null(draft.InvalidField);
        Assert.True(draft.IsDirty);
        Assert.Equal("2.25", draft.ShadowXText);
        Assert.Equal("8.125", draft.ShadowYText);
        var changed = draft.Read(original);
        Assert.Equal(original with { Fill = fill, ShadowOffset = new(2.25, 8.125) }, changed);
        var typographyFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(SubtitleStyle.FontFamily), nameof(SubtitleStyle.FontSize), nameof(SubtitleDetailsStyleDraft.FontSizeText),
            nameof(SubtitleStyle.Bold), nameof(SubtitleStyle.Italic), nameof(SubtitleStyle.Underline), nameof(SubtitleStyle.Strikethrough), "Emphasis"
        };
        Assert.DoesNotContain(typeof(SubtitleKaraokeStyleDraft).GetProperties(), property => typographyFields.Contains(property.Name));
        Assert.False(body.IsDirty);
        Assert.False(body.Read(bodyStyle).HasOverrides);
        Assert.Equal(bodyStyle, body.Read(bodyStyle).ApplyTo(bodyStyle));
    }

    [Fact]
    public void EditingAndLoadingHighlightClearsFailureMetadata()
    {
        var original = new KaraokeHighlightStyle { PresetId = Guid.NewGuid(), PresetName = "Custom" };
        var draft = new SubtitleKaraokeStyleDraft();
        draft.Load(original);
        draft.StrokeWidthText = "-";
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal(nameof(SubtitleKaraokeStyleDraft.StrokeWidthText), draft.InvalidField);
        draft.StrokeWidthText = "3";
        Assert.Null(draft.InvalidField);
        Assert.Equal(3, draft.Read(original).StrokeWidth);
        Assert.Null(draft.InvalidField);
        draft.ShadowYText = "7e-";
        Assert.Throws<InvalidDataException>(() => draft.Read(original));
        Assert.Equal(nameof(SubtitleKaraokeStyleDraft.ShadowYText), draft.InvalidField);
        draft.Load(original);
        Assert.Null(draft.InvalidField);
        Assert.False(draft.IsDirty);
    }
}
