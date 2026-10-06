using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class PathOperationsTests
{
    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void InsertedPointPreservesTheCompleteBezierGeometry(double splitProgress)
    {
        var path = new PathGeometry(new(10, 30), [new(new(100, -20), new(150, 200), new(300, 70))]);
        var split = PathOperations.SplitSegment(path, 0, splitProgress);
        Assert.Equal(2, split.Segments.Length);
        for (var index = 0; index <= 100; index++)
        {
            var progress = index / 100d;
            var expected = SceneEvaluator.EvaluatePath(path, progress);
            var mapped = progress <= splitProgress ? 0.5 * progress / splitProgress :
                0.5 + 0.5 * (progress - splitProgress) / (1 - splitProgress);
            var actual = SceneEvaluator.EvaluatePath(split, mapped);
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
        }

        Assert.Single(path.Segments);
    }

    [Fact]
    public void MultiplePointsCanBeAppendedAndRemovedWithAStableMinimum()
    {
        var original = new PathGeometry(new(0, 0), [new(new(10, 0), new(20, 0), new(30, 0))]);
        var path = PathOperations.AppendPoint(PathOperations.AppendPoint(original, new(60, 20)), new(90, 40));
        Assert.Equal(3, path.Segments.Length);
        Assert.Equal(new ScenePoint(90, 40), path.Segments[^1].End);
        path = PathOperations.RemovePoint(path, 1);
        Assert.Equal(2, path.Segments.Length);
        Assert.Equal(new ScenePoint(60, 20), path.Segments[0].End);
        path = PathOperations.RemovePoint(path, 0);
        Assert.Equal(new ScenePoint(60, 20), path.Start);
        Assert.Single(path.Segments);
        Assert.Throws<InvalidOperationException>(() => PathOperations.RemovePoint(path, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathOperations.AppendPoint(path, new(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PathOperations.SplitSegment(path, 0, 0));
    }
}
