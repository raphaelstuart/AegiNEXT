using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SystemFontResolverTests
{
    [Fact]
    public void PostScriptIdentityWinsBeforeVariantNameOrNumericStyle()
    {
        var extraBold = Face("ExtraBold", 800, "Example-ExtraBold");
        var black = Face("Black", 900, "Example-Black");
        var resolver = new SystemFontResolver(new([extraBold, black]));
        Assert.Equal(black.Variant, resolver.Match("Example", extraBold.Variant with { PostScriptName = "Example-Black" })!.Variant);
    }

    [Fact]
    public void VariantNameAndDesignerFeaturesMatchWhenPostScriptNameIsUnavailable()
    {
        var black = Face("Black", 900, null);
        var resolver = new SystemFontResolver(new([Face("ExtraBold", 800, null), black]));
        Assert.Equal(black.Variant, resolver.Match("Example alias", black.Variant with { Name = "B l a c k" })!.Variant);
        Assert.Null(resolver.Match("Example", black.Variant with { Weight = 800 }));
    }

    [Fact]
    public void RequestedNativeStyleKeepsVariantWeightWhenBoldButtonIsOff()
    {
        using var style = SystemFontResolver.FontStyle(new()
        {
            Bold = false, Italic = true, FontVariant = new() { Name = "Black", Weight = 900, Width = 4 }
        });
        Assert.Equal(900, style.Weight);
        Assert.Equal(4, style.Width);
        Assert.Equal(SKFontStyleSlant.Italic, style.Slant);
    }

    [MacInstalledNotoFontFact]
    public void IndependentAndVariableBlackResolveTheirExactFacesRatherThanExtraBold()
    {
        var catalog = Catalog();
        var resolver = new SystemFontResolver(catalog);
        foreach (var family in new[] { "Noto Sans SC", "Noto Sans", "Noto Serif SC" })
        {
            var black = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == "Black");
            var resolved = resolver.Resolve(Style(black));
            using var typeface = resolved.Typeface;
            Assert.True(resolved.IsExactMatch);
            Assert.NotNull(resolved.Face);
            Assert.Equal("Black", resolved.Face.Variant.Name);
            Assert.Equal(900, resolved.Face.Variant.Weight);
            Assert.Equal(black.IsVariable, resolved.Face.IsVariable);
            Assert.Equal(black.NamedInstanceIndex, resolved.Face.NamedInstanceIndex);
        }
    }

    [MacInstalledNotoFontFact]
    public void MissingVariantKeepsSavedIdentityAndFallsBackToClosestDesignerWeight()
    {
        var catalog = Catalog();
        var resolver = new SystemFontResolver(catalog);
        var style = new SubtitleStyle
        {
            FontFamily = "Noto Sans", FontVariant = new() { Name = "Removed Black", PostScriptName = "Removed-Black", Weight = 900 }, Bold = false
        };
        var before = style;
        var resolved = resolver.Resolve(style);
        using var typeface = resolved.Typeface;
        Assert.False(resolved.IsExactMatch);
        Assert.Equal(900, resolved.Face!.Variant.Weight);
        Assert.Equal("Black", resolved.Face.Variant.Name);
        Assert.Equal(before, style);
        Assert.Equal("Removed-Black", style.FontVariant!.Value.PostScriptName);
    }

    [MacInstalledNotoFontFact]
    public void NamedVariantDoesNotSynthesizeBoldButRetainsItalicFallback()
    {
        var catalog = Catalog();
        var regular = Assert.Single(catalog.Faces, face => face.FamilyName == "Noto Serif SC" && face.Variant.Name == "Regular");
        var style = Style(regular) with { Bold = true, Italic = true };
        var resolved = new SystemFontResolver(catalog).Resolve(style);
        using var shaper = new TextShaper(resolved.Typeface, style.Bold, style.Italic, resolved.Face, true);
        Assert.False(shaper.SynthesizesBold);
        Assert.True(shaper.SynthesizesItalic);
    }

    private static SystemFontFace Face(string name, int weight, string? postScriptName)
    {
        return new()
        {
            FamilyName = "Example", Aliases = ["Example", "Example alias"], SourceFamilyName = "Example",
            Variant = new() { Name = name, Weight = weight, PostScriptName = postScriptName }, IsStyleEnumerated = true
        };
    }

    private static SystemFontCatalog Catalog()
    {
        return new(SKFontManager.Default, ["Noto Sans SC", "Noto Sans", "Noto Serif SC"]);
    }

    private static SubtitleStyle Style(SystemFontFace face)
    {
        return new() { FontFamily = face.FamilyName, FontVariant = face.Variant, Bold = face.Variant.Weight >= 700, Italic = face.Variant.Italic };
    }
}
