using AegiNext.Rendering.Fonts;
using HarfBuzzSharp;
using SkiaSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace AegiNext.Rendering.Tests;

public sealed class NamedInstanceTextShaperTests
{
    private static readonly uint[] accentedLigatureClusters = [0, 1];
    [Fact]
    public void PinnedVariableFixtureUsesNamedInstanceMetricsInsteadOfDefaultMetrics()
    {
        using var typeface = Fixture();
        using var font = new SKFont(typeface, 64);
        using var regular = new NamedInstanceTextShaper(typeface, 4, 0);
        using var black = new NamedInstanceTextShaper(typeface, 9, 0);
        using var regularBuffer = Text("W");
        using var blackBuffer = Text("W");
        var regularResult = regular.Shape(regularBuffer, font);
        var blackResult = black.Shape(blackBuffer, font);
        Assert.True(blackResult.Width > regularResult.Width);
        Assert.Equal(regularResult.Codepoints, blackResult.Codepoints);
        Assert.Equal(regularResult.Clusters, blackResult.Clusters);
        using var ordinary = new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf")));
        using var expectedRegular = ordinary.Shape("W", 64, TextDirection.LEFT_TO_RIGHT, "en");
        Assert.Equal(expectedRegular.AdvanceWidth, regularResult.Width);
    }

    [Fact]
    public void NamedInstanceShapingRetainsLigaturesAndUtf16Clusters()
    {
        using var typeface = Fixture();
        using var font = new SKFont(typeface, 32);
        using var shaper = new NamedInstanceTextShaper(typeface, 6, 0);
        using var buffer = Text("éffi");
        var result = shaper.Shape(buffer, font);
        Assert.Equal(2, result.Codepoints.Length);
        Assert.Equal(accentedLigatureClusters, result.Clusters);
        Assert.True(result.Width > 0);
    }

    [MacInstalledNotoFontFact]
    public void RealVariableDrawingFaceAndNamedHarfBuzzInstanceHaveMatchingAdvance()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Noto Sans", "Noto Serif SC"]);
        var resolver = new SystemFontResolver(catalog);
        foreach (var family in new[] { "Noto Sans", "Noto Serif SC" })
        {
            foreach (var name in new[] { "SemiBold", "Black" })
            {
                var face = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == name);
                var resolved = resolver.Resolve(new() { FontFamily = family, FontVariant = face.Variant });
                using var typeface = resolved.Typeface;
                using var font = new SKFont(typeface, 64) { Hinting = SKFontHinting.None, Subpixel = true };
                using var named = new NamedInstanceTextShaper(typeface, resolved.Face!.NamedInstanceIndex, resolved.Face.CollectionIndex);
                using var buffer = Text("W");
                var shaped = named.Shape(buffer, font);
                var width = Assert.Single(font.GetGlyphWidths(new ushort[] { (ushort)shaped.Codepoints[0] }));
                Assert.InRange(Math.Abs(width - shaped.Width), 0, 0.13f);
            }
        }
    }

    [Fact]
    public void DisposedNamedShaperRejectsFurtherShaping()
    {
        using var typeface = Fixture();
        using var font = new SKFont(typeface, 32);
        var shaper = new NamedInstanceTextShaper(typeface, 9, 0);
        shaper.Dispose();
        shaper.Dispose();
        using var buffer = Text("W");
        Assert.Throws<ObjectDisposedException>(() => shaper.Shape(buffer, font));
    }

    private static SKTypeface Fixture()
    {
        return SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
    }

    private static Buffer Text(string text)
    {
        var buffer = new Buffer();
        buffer.AddUtf16(text);
        buffer.Direction = Direction.LeftToRight;
        buffer.Language = new("en");
        buffer.GuessSegmentProperties();
        return buffer;
    }
}
