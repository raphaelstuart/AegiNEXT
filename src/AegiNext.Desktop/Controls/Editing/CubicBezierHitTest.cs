using Avalonia;

namespace AegiNext.Desktop.Controls;

internal static class CubicBezierHitTest
{
    internal static (double Progress, Point Position, double DistanceSquared)? FindNearest(
        Point start, Point control1, Point control2, Point end, Point pointer, double tolerance)
    {
        var bestDistance = tolerance * tolerance;
        var bestProgress = 0d;
        var bestPosition = start;
        var found = false;
        void Include(double progress)
        {
            var position = Evaluate(start, control1, control2, end, progress);
            var distance = DistanceSquared(position, pointer);
            if (distance < bestDistance || !found && distance <= bestDistance)
            {
                bestDistance = distance;
                bestProgress = progress;
                bestPosition = position;
                found = true;
            }
        }
        Include(0);
        Include(1);
        var pending = new Stack<(Point Start, Point Control1, Point Control2, Point End, double From, double To, int Depth)>();
        pending.Push((start, control1, control2, end, 0, 1, 0));
        while (pending.TryPop(out var segment))
        {
            var left = Math.Min(Math.Min(segment.Start.X, segment.Control1.X), Math.Min(segment.Control2.X, segment.End.X));
            var right = Math.Max(Math.Max(segment.Start.X, segment.Control1.X), Math.Max(segment.Control2.X, segment.End.X));
            var top = Math.Min(Math.Min(segment.Start.Y, segment.Control1.Y), Math.Min(segment.Control2.Y, segment.End.Y));
            var bottom = Math.Max(Math.Max(segment.Start.Y, segment.Control1.Y), Math.Max(segment.Control2.Y, segment.End.Y));
            var boxDistance = new Vector(pointer.X - Math.Clamp(pointer.X, left, right), pointer.Y - Math.Clamp(pointer.Y, top, bottom)).SquaredLength;
            if (boxDistance > bestDistance)
            {
                continue;
            }
            if (segment.Depth >= 32 || Math.Max(DistanceToSegment(segment.Control1, segment.Start, segment.End),
                DistanceToSegment(segment.Control2, segment.Start, segment.End)) <= 0.0625)
            {
                var from = segment.From;
                var to = segment.To;
                const double GOLDEN_RATIO = 0.6180339887498949;
                var a = to - (to - from) * GOLDEN_RATIO;
                var b = from + (to - from) * GOLDEN_RATIO;
                var distanceA = DistanceSquared(Evaluate(start, control1, control2, end, a), pointer);
                var distanceB = DistanceSquared(Evaluate(start, control1, control2, end, b), pointer);
                for (var iteration = 0; iteration < 64; iteration++)
                {
                    if (distanceA < distanceB)
                    {
                        to = b;
                        b = a;
                        distanceB = distanceA;
                        a = to - (to - from) * GOLDEN_RATIO;
                        distanceA = DistanceSquared(Evaluate(start, control1, control2, end, a), pointer);
                    }
                    else
                    {
                        from = a;
                        a = b;
                        distanceA = distanceB;
                        b = from + (to - from) * GOLDEN_RATIO;
                        distanceB = DistanceSquared(Evaluate(start, control1, control2, end, b), pointer);
                    }
                }
                Include((from + to) / 2);
                continue;
            }
            var p = Midpoint(segment.Start, segment.Control1);
            var q = Midpoint(segment.Control1, segment.Control2);
            var r = Midpoint(segment.Control2, segment.End);
            var s = Midpoint(p, q);
            var t = Midpoint(q, r);
            var middle = Midpoint(s, t);
            var progress = (segment.From + segment.To) / 2;
            pending.Push((middle, t, r, segment.End, progress, segment.To, segment.Depth + 1));
            pending.Push((segment.Start, p, s, middle, segment.From, progress, segment.Depth + 1));
        }
        return found ? (bestProgress, bestPosition, bestDistance) : null;
    }

    private static Point Evaluate(Point start, Point control1, Point control2, Point end, double progress)
    {
        var u = 1 - progress;
        return new(
            u * u * u * start.X + 3 * u * u * progress * control1.X + 3 * u * progress * progress * control2.X + progress * progress * progress * end.X,
            u * u * u * start.Y + 3 * u * u * progress * control1.Y + 3 * u * progress * progress * control2.Y + progress * progress * progress * end.Y);
    }

    private static Point Midpoint(Point first, Point second) => new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static double DistanceSquared(Point first, Point second) => new Vector(first.X - second.X, first.Y - second.Y).SquaredLength;

    private static double DistanceToSegment(Point point, Point first, Point second)
    {
        var delta = new Vector(second.X - first.X, second.Y - first.Y);
        var offset = new Vector(point.X - first.X, point.Y - first.Y);
        var progress = delta.SquaredLength > 0 ? Math.Clamp(Vector.Dot(offset, delta) / delta.SquaredLength, 0, 1) : 0;
        return DistanceSquared(point, first + delta * progress);
    }
}
