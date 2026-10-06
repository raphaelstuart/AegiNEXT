using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleFontVariantTests
{
    [Fact]
    public void DefaultValueIsRejectedAndConstructedValueUsesRegularStyleDefaults()
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { FontVariant = default(SubtitleFontVariant) }));
        var variant = new SubtitleFontVariant { Name = "Regular" };
        Assert.Equal(400, variant.Weight);
        Assert.Equal(5, variant.Width);
        ProjectValidator.ValidateSubtitleStyle(new() { FontVariant = variant });
    }

    [Fact]
    public void VisualOverridesInheritTheVariantAndFontChangesClearIt()
    {
        var style = Style();
        Assert.Equal(style, new SubtitleInlineStyleOverride().ApplyTo(style));
        Assert.Equal(style.FontVariant, new SubtitleInlineStyleOverride { Fill = SceneColor.Black }.ApplyTo(style).FontVariant);
        Assert.Null(new SubtitleInlineStyleOverride { ClearFontVariant = true }.ApplyTo(style).FontVariant);
        Assert.Null(new SubtitleInlineStyleOverride { FontFamily = "serif" }.ApplyTo(style).FontVariant);
        Assert.Null(new SubtitleInlineStyleOverride { FontAssetId = Guid.NewGuid() }.ApplyTo(style).FontVariant);
        Assert.Null(new SubtitleInlineStyleOverride { Bold = true }.ApplyTo(style).FontVariant);
        Assert.Null(new SubtitleInlineStyleOverride { Italic = true }.ApplyTo(style).FontVariant);
        Assert.Equal(style.FontVariant, new SubtitleInlineStyleOverride { Bold = false }.ApplyTo(style).FontVariant);
    }

    [Fact]
    public void SelectingAVariantClearsEmbeddedFontAndSynchronizesFormatting()
    {
        var variant = Variant() with { Name = "Black Italic", Weight = 900, Italic = true };
        var resolved = new SubtitleInlineStyleOverride { FontVariant = variant }.ApplyTo(new() { FontAssetId = Guid.NewGuid() });
        Assert.Equal(variant, resolved.FontVariant);
        Assert.Null(resolved.FontAssetId);
        Assert.True(resolved.Bold);
        Assert.True(resolved.Italic);
    }

    [Fact]
    public void MergePreservesVisualChangesAndReplacesOrClearsFontIdentity()
    {
        var original = new SubtitleInlineStyleOverride { FontFamily = "Example Sans", FontVariant = Variant() };
        var colored = original.Merge(new() { Fill = SceneColor.Black });
        Assert.Equal(original.FontVariant, colored.FontVariant);
        Assert.Equal(SceneColor.Black, colored.Fill);
        Assert.Null(original.Merge(new() { FontFamily = "serif" }).ApplyTo(Style()).FontVariant);
        Assert.Null(original.Merge(new() { ClearFontVariant = true }).ApplyTo(Style()).FontVariant);
        var replacement = Variant() with { Name = "Black", Weight = 900 };
        Assert.Equal(replacement, original.Merge(new() { FontVariant = replacement }).ApplyTo(Style()).FontVariant);
        Assert.Equal(replacement, new SubtitleInlineStyleOverride { ClearFontVariant = true }
            .Merge(new() { FontVariant = replacement }).ApplyTo(Style()).FontVariant);
    }

    [Fact]
    public void CompleteStyleOverridesPreserveVariantsOrExplicitlyClearInheritance()
    {
        var style = Style();
        Assert.Equal(style, SubtitleInlineStyleOverride.FromStyle(style).ApplyTo(new()));
        var regular = style with { FontVariant = null };
        var overlay = SubtitleInlineStyleOverride.FromStyle(regular);
        Assert.True(overlay.ClearFontVariant);
        Assert.Equal(regular, overlay.ApplyTo(style));
    }

    [Fact]
    public void SelectingANewVariantReplacesPreviouslyExplicitFormattingAndAssetOverrides()
    {
        var original = new SubtitleInlineStyleOverride
        {
            FontAssetId = Guid.NewGuid(), FontVariant = Variant() with { Weight = 900, Italic = true },
            Bold = true, Italic = true
        };
        var resolved = original.Merge(new() { FontVariant = Variant() }).ApplyTo(new());
        Assert.Equal(Variant(), resolved.FontVariant);
        Assert.Null(resolved.FontAssetId);
        Assert.False(resolved.Bold);
        Assert.False(resolved.Italic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidVariantMetadataIsRejected(int invalid)
    {
        var variant = invalid switch
        {
            0 => Variant() with { Name = "" },
            1 => Variant() with { Name = "bad\u0000name" },
            2 => Variant() with { PostScriptName = "bad\u0000name" },
            3 => Variant() with { Weight = 0 },
            4 => Variant() with { Weight = 1001 },
            5 => Variant() with { Width = 0 },
            _ => Variant() with { Width = 10 }
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(Style() with { FontVariant = variant }));
    }

    [Fact]
    public void ConflictingVariantAndClearAreRejected()
    {
        var line = new SubtitleLine { Text = "a", InlineSpans = [new(0, 1, new() { FontVariant = Variant(), ClearFontVariant = true })] };
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
    }

    private static SubtitleStyle Style() => new() { FontFamily = "Example Sans", FontVariant = Variant() };

    private static SubtitleFontVariant Variant() => new() { Name = "SemiBold", PostScriptName = "ExampleSans-SemiBold", Weight = 600 };
}
