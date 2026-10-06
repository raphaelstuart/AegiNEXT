using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests;

public sealed class EffectCanvasControlTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void MovingHandleChangesExactlyOnePointInAnImmutablePath(int index)
    {
        var original = CreatePath();
        var before = Points(original);
        var changed = EffectCanvasControl.MoveHandle(original, index, new(10, -3));
        var after = Points(changed);
        Assert.True(changed.Closed);
        Assert.Equal(before, Points(original));
        for (var point = 0; point < before.Length; point++)
        {
            Assert.Equal(point == index ? new(before[point].X + 10, before[point].Y - 3) : before[point], after[point]);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void InvalidHandleIndexDoesNotMutateAnotherHandle(int index)
    {
        var original = CreatePath();
        var before = Points(original);
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectCanvasControl.MoveHandle(original, index, new(1, 2)));
        Assert.Equal(before, Points(original));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void NonFiniteDragIsRejected(double x, double y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectCanvasControl.MoveHandle(CreatePath(), 0, new(x, y)));
    }

    [Fact]
    public void FiniteOffsetCannotOverflowTheMovedPoint()
    {
        var original = CreatePath() with { Start = new(double.MaxValue, 0) };
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectCanvasControl.MoveHandle(original, 0, new(double.MaxValue, 0)));
    }

    private static PathGeometry CreatePath()
    {
        return new(new(0, 1), [new(new(2, 3), new(4, 5), new(6, 7)), new(new(8, 9), new(10, 11), new(12, 13))], true);
    }

    private static ScenePoint[] Points(PathGeometry path)
    {
        return new[] { path.Start }.Concat(path.Segments.SelectMany(segment => new[] { segment.Control1, segment.Control2, segment.End })).ToArray();
    }
}
