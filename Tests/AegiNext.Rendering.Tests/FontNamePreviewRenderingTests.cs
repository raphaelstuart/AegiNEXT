using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class FontNamePreviewRenderingTests
{
    [Fact]
    public void PinnedDefaultNamedInstanceKeepsTheActualSingleGlyphAdvance()
    {
        using var regular = FixtureFace("Regular");
        var preview = Assert.IsType<FontNamePreview>(regular.Render(Request("W"), default));
        using var expected =
            new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf")));
        using var shape = expected.Shape("W", 20, TextDirection.LEFT_TO_RIGHT, "und");
        var expectedWidth = Math.Max(shape.AdvanceWidth, shape.InkBounds.Right) - Math.Min(0, shape.InkBounds.Left);
        Assert.InRange(preview.LogicalWidth - expectedWidth, 2, 3);
        Assert.Contains(preview.Alpha.ToArray(), value => value > 0);
    }

    [Fact]
    public void PhysicalPixelsFollowDpiWhileLogicalMenuSizeRemainsStable()
    {
        using var face = FixtureFace("Regular");
        var first = Assert.IsType<FontNamePreview>(face.Render(Request("Regular"), default));
        var second = Assert.IsType<FontNamePreview>(face.Render(Request("Regular", 2), default));
        Assert.InRange(second.PixelWidth, first.PixelWidth * 2 - 3, first.PixelWidth * 2 + 3);
        Assert.InRange(second.PixelHeight, first.PixelHeight * 2 - 3, first.PixelHeight * 2 + 3);
        Assert.InRange(Math.Abs(first.LogicalWidth - second.LogicalWidth), 0, 2);
        Assert.InRange(Math.Abs(first.LogicalHeight - second.LogicalHeight), 0, 2);
    }

    [Fact]
    public void LongNamesFitTheLogicalWidthAndRetainTransparentPadding()
    {
        using var face = FixtureFace("Regular");
        var request = Request(new string('W', 100), 2) with { MaxWidth = 100.25 };
        var preview = Assert.IsType<FontNamePreview>(face.Render(request, default));
        Assert.InRange(preview.LogicalWidth, 1, request.MaxWidth);
        Assert.InRange(preview.PixelWidth, 1, 200);
        Assert.Equal(preview.PixelWidth * preview.PixelHeight, preview.Alpha.Length);
        var pixels = preview.Alpha.ToArray();
        Assert.All(pixels.Take(preview.PixelWidth), value => Assert.Equal(0, value));
        Assert.All(pixels.TakeLast(preview.PixelWidth), value => Assert.Equal(0, value));
        for (var row = 0; row < preview.PixelHeight; row++)
        {
            Assert.Equal(0, pixels[row * preview.PixelWidth]);
            Assert.Equal(0, pixels[(row + 1) * preview.PixelWidth - 1]);
        }

        Assert.Contains(pixels, value => value > 0);
    }

    [Fact]
    public void MissingGlyphsReturnNoPreviewInsteadOfADeceptiveFallbackFont()
    {
        using var face = FixtureFace("Regular");
        Assert.Null(face.Render(Request("\U0010FFFF"), default));
        Assert.Null(face.Render(Request("字幕"), default));
    }

    [Fact]
    public void EmptyCatalogAndUnavailableSavedVariantsCannotResolveANativeFallback()
    {
        var renderer = new SystemFontNamePreviewRenderer(() => SystemFontCatalog.Empty);
        var request = Request("Regular") with
        {
            FamilyName = "Missing Font",
            Variant = new() { Name = "Removed Black", Weight = 900, PostScriptName = "Removed-Black" }
        };
        Assert.Null(renderer.Resolve(request, default));
        Assert.Null(renderer.Resolve(request with { Variant = null }, default));
        Assert.Equal("Removed-Black", request.Variant!.Value.PostScriptName);
    }

    [Fact]
    public void ReopeningTheSameActualFaceKeepsItsPersistentFingerprint()
    {
        using var first = FixtureFace("Regular");
        using var second = FixtureFace("Regular");
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void AccentAndDescenderInkFitInsideTransparentTopAndBottomPadding()
    {
        using var face = FixtureFace("Regular");
        var preview = Assert.IsType<FontNamePreview>(face.Render(Request("Égj Åy", 2), default));
        var pixels = preview.Alpha.ToArray();
        Assert.All(pixels.Take(preview.PixelWidth), value => Assert.Equal(0, value));
        Assert.All(pixels.TakeLast(preview.PixelWidth), value => Assert.Equal(0, value));
        Assert.Contains(pixels, value => value > 0);
    }

    [Fact]
    public void UnrenderablePhysicalSizesReturnNoRasterAndInvalidDpiIsRejected()
    {
        using var face = FixtureFace("Regular");
        Assert.Null(face.Render(Request("Regular") with { MaxWidth = 0.5 }, default));
        Assert.Null(face.Render(Request("Regular") with { FontSize = double.MaxValue }, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            face.Render(Request("Regular") with { RenderScale = double.NaN }, default));
    }

    [Fact]
    public void CancelledAndDisposedFacesCannotProduceAStalePreview()
    {
        var face = FixtureFace("Regular");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            Assert.Throws<OperationCanceledException>(() => face.Render(Request("Regular"), cancellation.Token));
        }
        finally
        {
            face.Dispose();
        }

        face.Dispose();
        Assert.Throws<ObjectDisposedException>(() => face.Render(Request("Regular"), default));
    }

    [Fact]
    public void RasterRejectsMalformedManagedPayloadsBeforePresentation()
    {
        Assert.Throws<ArgumentException>(() => new FontNamePreview(2, 2, 2, 2, new byte[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontNamePreview(0, 2, 2, 2, new byte[4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontNamePreview(2, 2, double.NaN, 2, new byte[4]));
    }

    [MacFontNamePreviewFact]
    public void InstalledNamedVariantsResolveTheirExactFacesAndRenderChineseNames()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Noto Sans SC", "Noto Serif SC"]);
        var renderer = new SystemFontNamePreviewRenderer(() => catalog);
        foreach (var family in new[] { "Noto Sans SC", "Noto Serif SC" })
        {
            var previews = new List<FontNamePreview>();
            var fingerprints = new List<string>();
            foreach (var name in new[] { "Regular", "Black" })
            {
                var candidate = Assert.Single(catalog.Faces,
                    face => face.FamilyName == family && face.Variant.Name == name);
                var request = Request("字幕字重") with { FamilyName = family, Variant = candidate.Variant };
                using var resolved = Assert.IsAssignableFrom<IFontNamePreviewFace>(renderer.Resolve(request, default));
                previews.Add(Assert.IsType<FontNamePreview>(resolved.Render(request, default)));
                fingerprints.Add(resolved.Fingerprint);
                if (candidate.IsVariable)
                {
                    using var native = SKFontManager.Default.GetFontStyles(candidate.SourceFamilyName);
                    using var typeface = native.CreateTypeface(candidate.StyleIndex);
                    using var shaper = new TextShaper(typeface, face: candidate, explicitVariant: true);
                    using var expected = shaper.Shape(request.Text, (float)request.FontSize,
                        TextDirection.LEFT_TO_RIGHT, "und");
                    var expectedWidth = Math.Max(expected.AdvanceWidth, expected.InkBounds.Right) -
                                        Math.Min(0, expected.InkBounds.Left);
                    Assert.InRange(previews[^1].LogicalWidth - expectedWidth, 2, 3);
                }
            }

            Assert.NotEqual(fingerprints[0], fingerprints[1]);
            Assert.False(previews[0].Alpha.Span.SequenceEqual(previews[1].Alpha.Span));
            using var familyFace = Assert.IsAssignableFrom<IFontNamePreviewFace>(renderer.Resolve(
                Request(family) with { FamilyName = family }, default));
            Assert.Equal(fingerprints[0], familyFace.Fingerprint);
            var removed = Request("Removed Black") with
            {
                FamilyName = family,
                Variant = new() { Name = "Removed Black", Weight = 900, PostScriptName = "Removed-Black" }
            };
            Assert.Null(renderer.Resolve(removed, default));
        }
    }

    private static FontNamePreviewRequest Request(string text, double scale = 1)
    {
        return new("Noto Sans", null, text, 20, scale, 500);
    }

    private static SystemFontNamePreviewFace FixtureFace(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf");
        SystemFontFace face;
        using (var original = SKTypeface.FromFile(path))
        {
            face = Assert.Single(SystemFontCatalog.ReadTypeface(original, "Noto Sans", 0, null),
                candidate => candidate.Variant.Name == name);
            Assert.True(face.IsStyleEnumerated);
        }

        var typeface = SKTypeface.FromFile(path);
        return Assert.IsType<SystemFontNamePreviewFace>(SystemFontNamePreviewFace.TryCreate(typeface, face, default));
    }
}
