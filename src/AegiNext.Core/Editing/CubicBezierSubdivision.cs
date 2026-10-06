using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal static class CubicBezierSubdivision
{
    internal static (CubicBezierSegment Before, CubicBezierSegment After) Split(ScenePoint start, CubicBezierSegment segment, double progress)
    {
        var a = Lerp(start, segment.Control1, progress);
        var b = Lerp(segment.Control1, segment.Control2, progress);
        var c = Lerp(segment.Control2, segment.End, progress);
        var d = Lerp(a, b, progress);
        var e = Lerp(b, c, progress);
        var point = Lerp(d, e, progress);
        return (new(a, d, point), new(e, c, segment.End));
    }

    private static ScenePoint Lerp(ScenePoint first, ScenePoint second, double progress) => new(
        first.X + (second.X - first.X) * progress, first.Y + (second.Y - first.Y) * progress);
}
