using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

internal static class AnimationTrackSlicer
{
    internal static AnimationTrack Clip(AnimationTrack track, MediaTime minimum, MediaTime maximum)
    {
        return SliceCore(track, minimum, maximum, false);
    }

    internal static AnimationTrack Slice(AnimationTrack track, MediaTime minimum, MediaTime maximum)
    {
        return SliceCore(track, minimum, maximum, true);
    }

    private static AnimationTrack SliceCore(AnimationTrack track, MediaTime minimum, MediaTime maximum, bool includeBoundaries)
    {
        if (track.IsOrdered)
        {
            return track;
        }

        var frames = track.Keyframes;
        if (!includeBoundaries && frames[0].Time >= minimum && frames[^1].Time <= maximum)
        {
            return track;
        }

        var times = frames.Where(frame => frame.Time >= minimum && frame.Time <= maximum).Select(frame => frame.Time).ToList();
        if ((includeBoundaries || frames[0].Time < minimum) && !times.Contains(minimum))
        {
            times.Insert(0, minimum);
        }

        if ((includeBoundaries || frames[^1].Time > maximum) && !times.Contains(maximum))
        {
            times.Add(maximum);
        }

        var result = ImmutableArray.CreateBuilder<Keyframe>(times.Count);
        for (var index = 0; index < times.Count; index++)
        {
            var time = times[index];
            var original = frames.FirstOrDefault(frame => frame.Time == time);
            var key = original ?? new(time, SceneEvaluator.EvaluateTrack(track, time));
            if (index + 1 < times.Count)
            {
                var end = times[index + 1];
                var sourceIndex = -1;
                for (var segment = 0; segment + 1 < frames.Length; segment++)
                {
                    if (time >= frames[segment].Time && time < frames[segment + 1].Time)
                    {
                        sourceIndex = segment;
                        break;
                    }
                }

                if (sourceIndex >= 0)
                {
                    var first = frames[sourceIndex];
                    var second = frames[sourceIndex + 1];
                    var duration = Seconds(second.Time - first.Time);
                    var range = first.CurveEnd - first.CurveStart;
                    key = key with
                    {
                        Interpolation = first.Interpolation,
                        Exponent = first.Exponent,
                        CurveStart = Math.Clamp(first.CurveStart + range * Seconds(time - first.Time) / duration, first.CurveStart, first.CurveEnd),
                        CurveEnd = Math.Clamp(first.CurveStart + range * Seconds(end - first.Time) / duration, first.CurveStart, first.CurveEnd),
                        ComponentCurves = first.ComponentCurves.Select(curve => curve is null ? null : curve with
                        {
                            CurveStart = Math.Clamp(curve.CurveStart + (curve.CurveEnd - curve.CurveStart) * Seconds(time - first.Time) / duration,
                                curve.CurveStart, curve.CurveEnd),
                            CurveEnd = Math.Clamp(curve.CurveStart + (curve.CurveEnd - curve.CurveStart) * Seconds(end - first.Time) / duration,
                                curve.CurveStart, curve.CurveEnd)
                        }).ToImmutableArray()
                    };
                }
                else
                {
                    key = key with { Interpolation = KeyframeInterpolation.HOLD, CurveStart = 0, CurveEnd = 1, Exponent = 1, ComponentCurves = [] };
                }
            }

            result.Add(key);
        }

        return track with { Keyframes = result.MoveToImmutable() };
    }

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;

}
