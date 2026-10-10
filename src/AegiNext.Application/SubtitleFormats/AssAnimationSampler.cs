using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal static class AssAnimationSampler
{
    private const int MAX_SAMPLES = 4096;
    private const double MAX_ERROR = 0.5 / 255;
    private static readonly double[] sampleFractions = [0.25, 0.5, 0.75];

    internal static (ImmutableArray<Keyframe> Frames, bool Limited) Sample(AnimationTrack track,
        Func<AnimationValue, double[]> components, MediaTime origin, MediaTime end)
    {
        var duration = Math.Max(1, Milliseconds(end - origin));
        var offsets = new SortedSet<long> { 0, duration };
        foreach (var time in track.Keyframes.Select(frame => frame.Time)
            .Concat(track.Transforms.SelectMany(operation => new[] { operation.Start, operation.End })))
        {
            var offset = Milliseconds(time - origin);
            if (offset > 0 && offset < duration)
            {
                offsets.Add(offset);
            }
        }
        var values = new Dictionary<long, AnimationValue>();
        AnimationValue Value(long offset)
        {
            if (!values.TryGetValue(offset, out var value))
            {
                value = SceneEvaluator.EvaluateTrack(track, origin + new MediaTime(offset, 1000));
                values.Add(offset, value);
            }
            return value;
        }
        var queue = new Queue<(long Start, long End)>();
        var limited = offsets.Count > MAX_SAMPLES;
        if (limited)
        {
            var source = offsets.ToArray();
            offsets.Clear();
            for (var index = 0; index < MAX_SAMPLES; index++)
            {
                offsets.Add(source[(int)Math.Round(index * (source.Length - 1d) / (MAX_SAMPLES - 1))]);
            }
        }
        var boundaries = offsets.ToArray();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            queue.Enqueue((boundaries[index], boundaries[index + 1]));
        }
        while (queue.TryDequeue(out var interval))
        {
            var a = components(Value(interval.Start));
            var b = components(Value(interval.End));
            var error = false;
            foreach (var fraction in sampleFractions)
            {
                var sampleTime = origin + new MediaTime(checked(interval.Start * 4 +
                    (long)((interval.End - interval.Start) * fraction * 4)), 4000);
                var actual = components(SceneEvaluator.EvaluateTrack(track, sampleTime));
                error |= actual.Where((value, component) => Math.Abs(value - (a[component] + (b[component] - a[component]) * fraction)) > MAX_ERROR).Any();
            }
            if (!error)
            {
                continue;
            }
            if (interval.End - interval.Start <= 1 || offsets.Count >= MAX_SAMPLES)
            {
                limited = true;
                continue;
            }
            var middle = interval.Start + (interval.End - interval.Start) / 2;
            offsets.Add(middle);
            queue.Enqueue((interval.Start, middle));
            queue.Enqueue((middle, interval.End));
        }
        return (offsets.Select(offset => new Keyframe(origin + new MediaTime(offset, 1000), Value(offset))).ToImmutableArray(), limited);
    }

    private static long Milliseconds(MediaTime time) => time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
}
