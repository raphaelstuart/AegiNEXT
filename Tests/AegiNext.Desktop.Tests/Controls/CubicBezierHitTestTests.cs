using AegiNext.Desktop.Controls;
using Avalonia;

namespace AegiNext.Desktop.Tests.Controls;

public sealed class CubicBezierHitTestTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollinearBacktrackingAndVeryLongCurvesFindTheClickedPosition(bool veryLong)
    {
        var start = veryLong ? new Point(-100_000_000, 0) : new Point(0, 0);
        var control1 = veryLong ? start : new Point(300, 0);
        var end = veryLong ? new Point(100_000_000, 0) : new Point(100, 0);
        var control2 = veryLong ? end : new Point(-300, 0);
        var hit = CubicBezierHitTest.FindNearest(start, control1, control2, end, new(50, 0), 10);
        Assert.NotNull(hit);
        Assert.InRange(hit.Value.Progress, 0.00000001, 0.99999999);
        Assert.Equal(50, hit.Value.Position.X, 3);
        Assert.Equal(0, hit.Value.Position.Y, 3);
    }

    [Fact]
    public void ScreenDistanceAcceptsNearbyStrokeAndRejectsPointsOutsideTolerance()
    {
        var hit = CubicBezierHitTest.FindNearest(new(0, 0), new(0, 0), new(100, 0), new(100, 0), new(50, 9), 10);
        Assert.NotNull(hit);
        Assert.Equal(0.5, hit.Value.Progress, 7);
        Assert.Equal(50, hit.Value.Position.X, 6);
        Assert.Null(CubicBezierHitTest.FindNearest(new(0, 0), new(0, 0), new(100, 0), new(100, 0), new(50, 11), 10));
    }

    [Fact]
    public void CollapsedCurveKeepsTheExistingEndpointInsteadOfCreatingAnInteriorCandidate()
    {
        var node = new Point(10, 10);
        var hit = CubicBezierHitTest.FindNearest(node, node, node, node, node, 10);
        Assert.NotNull(hit);
        Assert.Equal(0, hit.Value.Progress);
    }
}
