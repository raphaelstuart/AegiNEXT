using System.Numerics;
using System.Security.Cryptography;

namespace AegiNext.Rendering.Tests;

public class TextShaperTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(-40)]
    public void LetterSpacingMovesWholeClustersWithoutBreakingLigaturesOrCombiningMarks(float spacing)
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var ordinary = shaper.Shape("A\u0301ffiB", 32, TextDirection.LEFT_TO_RIGHT, "en");
        using var spaced = shaper.Shape("A\u0301ffiB", 32, TextDirection.LEFT_TO_RIGHT, "en", spacing);
        Assert.Equal(ordinary.Glyphs.ToArray().Select(glyph => glyph.GlyphId), spaced.Glyphs.ToArray().Select(glyph => glyph.GlyphId));
        Assert.Equal(ordinary.Glyphs.ToArray().Select(glyph => glyph.Utf16Cluster), spaced.Glyphs.ToArray().Select(glyph => glyph.Utf16Cluster));
        var clusters = spaced.Clusters.ToArray();
        Assert.Equal(3, clusters.Length);
        Assert.Equal(ordinary.AdvanceWidth + (clusters.Length - 1) * spacing, spaced.AdvanceWidth, 4);
        Assert.Equal(ordinary.Glyphs[0].Position, spaced.Glyphs[0].Position);
        Assert.Equal(ordinary.Glyphs[^1].Position.X + 2 * spacing, spaced.Glyphs[^1].Position.X, 4);
        Assert.True(float.IsFinite(spaced.InkBounds.Left));
        Assert.True(spaced.InkBounds.Width > 0);
    }

    [Fact]
    public void ArabicSpacingPreservesContextualGlyphsAndItsVisualClusterOrder()
    {
        using var shaper = new TextShaper(ReadFont("NotoSansArabic.ttf"));
        using var ordinary = shaper.Shape("ببب", 32, TextDirection.RIGHT_TO_LEFT, "ar");
        using var spaced = shaper.Shape("ببب", 32, TextDirection.RIGHT_TO_LEFT, "ar", 9);
        Assert.Equal(ordinary.Glyphs.ToArray().Select(glyph => glyph.GlyphId), spaced.Glyphs.ToArray().Select(glyph => glyph.GlyphId));
        Assert.Equal(ordinary.Clusters.ToArray().Select(cluster => cluster.Utf16Start), spaced.Clusters.ToArray().Select(cluster => cluster.Utf16Start));
        Assert.Equal(ordinary.AdvanceWidth + 18, spaced.AdvanceWidth, 4);
    }

    [Fact]
    public void SpacedRunOwnsItsBlobAfterShaperDisposalAndReleasesItWhenDisposed()
    {
        var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        var run = shaper.Shape("AB", 32, TextDirection.LEFT_TO_RIGHT, "en", -64);
        shaper.Dispose();
        using var surface = new LinearRenderSurface(new(128, 64, 203));
        surface.DrawText(run, new(72, 44), new(1, 1, 1, 1));
        Assert.Contains(surface.CopySrgbBgra().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        run.Dispose();
        Assert.Throws<ObjectDisposedException>(() => run.GetBlob());
    }

    private static readonly int[] leftToRightClusters = [0, 1];
    private static readonly int[] rightToLeftClusters = [1, 0];
    private static readonly int[] supplementaryClusters = [0, 2];

    [Theory]
    [InlineData("NotoSans.ttf", "bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d")]
    [InlineData("NotoSansArabic.ttf", "63111b5b2e074dd48cc67692e0a2726d86ee94c1c37fe8598257b7b4e87e869e")]
    public void FixtureFontBytesArePinned(string name, string expectedHash)
    {
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(ReadFont(name))));
    }

    [Fact]
    public void LigaturesAreShapedBeforeRasterization()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var run = shaper.Shape("ffi", 32, TextDirection.LEFT_TO_RIGHT, "en");
        Assert.Single(run.Glyphs.ToArray());
        Assert.Equal(0, run.Glyphs[0].Utf16Cluster);
        Assert.True(run.AdvanceWidth > 0);
    }

    [Fact]
    public void CanonicallyEquivalentCombiningTextHasSameLayout()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var composed = shaper.Shape("é", 32, TextDirection.LEFT_TO_RIGHT, "fr");
        using var decomposed = shaper.Shape("e\u0301", 32, TextDirection.LEFT_TO_RIGHT, "fr");
        Assert.Equal(composed.Glyphs.ToArray(), decomposed.Glyphs.ToArray());
        Assert.Equal(composed.AdvanceWidth, decomposed.AdvanceWidth);
    }

    [Fact]
    public void ClustersUseUtf16RatherThanUtf8Offsets()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var run = shaper.Shape("éX", 32, TextDirection.LEFT_TO_RIGHT, "fr");
        Assert.Equal(leftToRightClusters, run.Glyphs.ToArray().Select(g => g.Utf16Cluster));
    }

    [Fact]
    public void SupplementaryScalarConsumesTwoUtf16CodeUnits()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var run = shaper.Shape("\U00010780X", 32, TextDirection.LEFT_TO_RIGHT, "und");
        Assert.Equal(supplementaryClusters, run.Glyphs.ToArray().Select(g => g.Utf16Cluster));
    }

    [Fact]
    public void ArabicUsesContextualFormsAndVisualRightToLeftOrder()
    {
        using var shaper = new TextShaper(ReadFont("NotoSansArabic.ttf"));
        using var joined = shaper.Shape("بب", 32, TextDirection.RIGHT_TO_LEFT, "ar");
        using var isolated = shaper.Shape("ب", 32, TextDirection.RIGHT_TO_LEFT, "ar");
        var joinedClusters = joined.Glyphs.ToArray().GroupBy(g => g.Utf16Cluster).ToArray();
        Assert.Equal(rightToLeftClusters, joinedClusters.Select(g => g.Key));
        var isolatedIds = isolated.Glyphs.ToArray().Select(g => g.GlyphId).ToArray();
        Assert.All(joinedClusters, cluster => Assert.False(cluster.Select(g => g.GlyphId).SequenceEqual(isolatedIds)));
        Assert.True(joined.AdvanceWidth > 0);
    }

    [Fact]
    public void FontSizeScalesGlyphPositionsAndAdvance()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var small = shaper.Shape("AB", 16, TextDirection.LEFT_TO_RIGHT, "en");
        using var large = shaper.Shape("AB", 32, TextDirection.LEFT_TO_RIGHT, "en");
        Assert.Equal(small.AdvanceWidth * 2, large.AdvanceWidth);
        Assert.Equal(small.Glyphs[1].Position * 2, large.Glyphs[1].Position);
    }

    [Fact]
    public void RunOwnsFontResourcesAndDrawsHdrAntialiasedTextAfterShaperDisposal()
    {
        var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        using var run = shaper.Shape("AegiNext ffi", 32, TextDirection.LEFT_TO_RIGHT, "en");
        shaper.Dispose();
        shaper.Dispose();
        using var surface = new LinearRenderSurface(new(256, 64, 203));
        surface.DrawText(run, new(4, 44), new(4, 2, 1, 0.5f));
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        var hasHighlight = false;
        var hasAntialiasedEdge = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = (float)pixels[i + 3];
            hasHighlight |= (float)pixels[i] > 1;
            hasAntialiasedEdge |= alpha is > 0 and < 0.49f;
            Assert.InRange(Math.Abs((float)pixels[i] - 4 * alpha), 0, 0.004f);
        }

        Assert.True(hasHighlight);
        Assert.True(hasAntialiasedEdge);
    }

    [Fact]
    public void MissingGlyphIsExplicitInsteadOfSystemFallback()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        Assert.Throws<InvalidOperationException>(() => shaper.Shape("\U0010ffff", 32, TextDirection.LEFT_TO_RIGHT, "und"));
    }

    [Fact]
    public void RejectsDamagedFont()
    {
        Assert.Throws<ArgumentException>(() => new TextShaper(new byte[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\u2028b")]
    public void RejectsUnsupportedTextBeforeNativeShaping(string text)
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        Assert.Throws<ArgumentException>(() => shaper.Shape(text, 32, TextDirection.LEFT_TO_RIGHT, "en"));
    }

    [Fact]
    public void RejectsUnpairedSurrogate()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        Assert.Throws<ArgumentException>(() => shaper.Shape(new string('\ud800', 1), 32, TextDirection.LEFT_TO_RIGHT, "en"));
    }

    [Fact]
    public void RejectsInvalidShapingOptions()
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        Assert.Throws<ArgumentOutOfRangeException>(() => shaper.Shape("A", float.NaN, TextDirection.LEFT_TO_RIGHT, "en"));
        Assert.Throws<ArgumentOutOfRangeException>(() => shaper.Shape("A", 0, TextDirection.LEFT_TO_RIGHT, "en"));
        Assert.Throws<ArgumentOutOfRangeException>(() => shaper.Shape("A", 32, (TextDirection)999, "en"));
        Assert.Throws<ArgumentException>(() => shaper.Shape("A", 32, TextDirection.LEFT_TO_RIGHT, ""));
    }

    [Theory]
    [InlineData(float.MaxValue)]
    [InlineData(float.Epsilon)]
    public void RejectsFontSizesThatCannotProduceFiniteUsableLayout(float size)
    {
        using var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        Assert.Throws<ArgumentOutOfRangeException>(() => shaper.Shape("A", size, TextDirection.LEFT_TO_RIGHT, "en"));
    }

    [Fact]
    public void DisposedResourcesCannotBeUsedForNativeOperations()
    {
        using var surface = new LinearRenderSurface(new(16, 16, 203));
        var shaper = new TextShaper(ReadFont("NotoSans.ttf"));
        var run = shaper.Shape("A", 12, TextDirection.LEFT_TO_RIGHT, "en");
        run.Dispose();
        run.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.DrawText(run, Vector2.Zero, default));
        shaper.Dispose();
        Assert.Throws<ObjectDisposedException>(() => shaper.Shape("A", 12, TextDirection.LEFT_TO_RIGHT, "en"));
    }

    private static byte[] ReadFont(string fileName)
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
    }
}
