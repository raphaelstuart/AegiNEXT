using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleTextBoundariesTests
{
    [Theory]
    [InlineData("", new[] { 0 })]
    [InlineData("abc", new[] { 0, 1, 2, 3 })]
    [InlineData("😀e\u0301\r\n👩‍💻🇨🇳", new[] { 0, 2, 4, 6, 11, 15 })]
    [InlineData("\u1100\u1161\u11A8", new[] { 0, 3 })]
    public void CompleteGraphemeBoundariesIncludeTheVirtualTextEnd(string text, int[] expected)
    {
        var boundaries = new SubtitleTextBoundaries(text);

        Assert.Same(text, boundaries.Text);
        Assert.Equal(expected.Length, boundaries.Length);
        Assert.Equal(expected, Enumerable.Range(0, boundaries.Length).Select(index => boundaries[index]));
        for (var offset = 0; offset <= text.Length; offset++)
        {
            var index = Array.BinarySearch(expected, offset);
            Assert.Equal(index >= 0, boundaries.Contains(offset));
            Assert.Equal(index, boundaries.IndexOf(offset));
        }
        Assert.Equal(text.Length, boundaries[^1]);
        Assert.False(boundaries.Contains(-1));
        Assert.False(boundaries.Contains(text.Length + 1));
        Assert.Equal(~0, boundaries.IndexOf(-1));
        Assert.Equal(~expected.Length, boundaries.IndexOf(text.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => boundaries[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => boundaries[boundaries.Length]);
    }

    [Fact]
    public void CheckingOnlyTextEndpointsDoesNotAllocateAnIndexForLargeText()
    {
        var text = new string('字', 1_000_001);
        var boundaries = new SubtitleTextBoundaries(text);
        Assert.True(boundaries.Contains(0));
        Assert.True(boundaries.Contains(text.Length));
        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(boundaries.Contains(0));
        Assert.True(boundaries.Contains(text.Length));
        Assert.False(boundaries.Contains(-1));
        Assert.False(boundaries.Contains(text.Length + 1));

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"Endpoint checks allocated {allocated} bytes.");
    }

    [Fact]
    public void RepeatedQueriesReuseTheSameTextIndex()
    {
        var boundaries = new SubtitleTextBoundaries(new string('字', 1_000_001));
        Assert.True(boundaries.Contains(500_000));
        Assert.Equal(500_000, boundaries.IndexOf(500_000));
        var before = GC.GetAllocatedBytesForCurrentThread();

        var index = boundaries.IndexOf(500_000);
        var offset = boundaries[index];
        var found = boundaries.Contains(500_000);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(500_000, offset);
        Assert.True(found);
        Assert.True(allocated < 1024, $"Repeated index queries allocated {allocated} bytes.");
    }
}
