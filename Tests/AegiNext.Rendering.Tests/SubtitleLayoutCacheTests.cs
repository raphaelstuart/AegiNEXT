using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleLayoutCacheTests
{
    [Fact]
    public void EvictionReleasesLeastRecentlyUsedLayoutWhileKeepingShaperAndRecentlyUsedBlobAlive()
    {
        using var shaper = new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf")));
        var cache = new SubtitleLayoutCache();
        var keys = new List<SubtitleLayoutKey>();
        var shapes = new List<ShapedTextRun>();
        try
        {
            for (var index = 0; index < 256; index++)
            {
                var key = new SubtitleLayoutKey(new() { Text = "A" }, 384, 160);
                var entry = Layout(shaper);
                keys.Add(key);
                shapes.Add(entry.Shape);
                cache.Add(key, entry.Layout, null);
            }
            Assert.True(cache.TryGet(keys[0], out _));
            var next = Layout(shaper);
            cache.Add(new(new() { Text = "A" }, 384, 160), next.Layout, null);

            Assert.Equal(256, cache.Count);
            Assert.True(cache.TryGet(keys[0], out _));
            Assert.False(cache.TryGet(keys[1], out _));
            Assert.Throws<ObjectDisposedException>(() => shapes[1].GetBlob());
            Assert.NotNull(shapes[0].GetBlob());
            using var another = shaper.Shape("B", 24, TextDirection.LEFT_TO_RIGHT, "en");
            Assert.NotNull(another.GetBlob());
            cache.Clear();
            Assert.Equal(0, cache.Count);
            Assert.Throws<ObjectDisposedException>(() => shapes[0].GetBlob());
            Assert.Throws<ObjectDisposedException>(() => next.Shape.GetBlob());
        }
        finally
        {
            cache.Clear();
        }
    }

    [Fact]
    public void ReplacingLatestAnimatedLayoutReleasesItsBlobAndPreservesStaticLayout()
    {
        using var shaper = new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf")));
        var cache = new SubtitleLayoutCache();
        var subtitle = new SubtitleLine { Text = "A" };
        var layerId = Guid.NewGuid();
        var fixedEntry = Layout(shaper);
        var first = Layout(shaper);
        var second = Layout(shaper);
        try
        {
            cache.Add(new(subtitle, 384, 160), fixedEntry.Layout, null);
            cache.Add(new(subtitle, 384, 160, 1), first.Layout, layerId);
            cache.Add(new(subtitle, 384, 160, 2), second.Layout, layerId);

            Assert.Equal(2, cache.Count);
            Assert.False(cache.TryGet(new(subtitle, 384, 160, 1), out _));
            Assert.True(cache.TryGet(new(subtitle, 384, 160, 2), out _));
            Assert.Throws<ObjectDisposedException>(() => first.Shape.GetBlob());
            Assert.NotNull(fixedEntry.Shape.GetBlob());
            Assert.NotNull(second.Shape.GetBlob());
        }
        finally
        {
            cache.Clear();
        }
    }

    private static (SubtitleLayout Layout, ShapedTextRun Shape) Layout(TextShaper shaper)
    {
        var shape = shaper.Shape("A", 24, TextDirection.LEFT_TO_RIGHT, "en");
        var bounds = shape.InkBounds;
        var run = new SubtitleLayoutRun("A", 0, new(), shape, TextDirection.LEFT_TO_RIGHT);
        var snapshot = new SubtitleTextLayout("A", bounds, SKPoint.Empty, SKPoint.Empty, true, [], [], bounds);
        var layout = new SubtitleLayout([new("A", 0, [run], 24, shape.AdvanceWidth)], bounds, SKPoint.Empty,
            SKPoint.Empty, true, snapshot);
        return (layout, shape);
    }
}
