using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Timing;

/// <summary>Processes filtered subtitle timings per track while preserving absolute animation and karaoke content origins.</summary>
public static class TimingPostProcessor
{
    /// <summary>Computes all enabled stages before atomically validating the final project; an unchanged result retains the input reference.</summary>
    public static ProjectDocument Process(ProjectDocument document, TimingPostProcessorOptions options,
        IReadOnlySet<string> styleNames, IReadOnlySet<Guid>? selectedIds = null, VideoTimingIndex? video = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(styleNames);
        ProjectValidator.Validate(document);
        options.Validate();
        var targets = document.Subtitles.Where(line => styleNames.Contains(line.StyleName) &&
            (selectedIds is null || selectedIds.Contains(line.Id))).ToArray();
        if (targets.Length == 0 || !options.LeadInEnabled && !options.LeadOutEnabled &&
            !options.AdjacencyEnabled && !options.KeyframeSnapEnabled)
        {
            return document;
        }

        if (options.KeyframeSnapEnabled && (video is null || video.Keyframes.IsEmpty))
        {
            throw new TimingPostProcessorException("Keyframe snapping requires an available video timing index.");
        }

        var timings = new Dictionary<Guid, (MediaTime Start, MediaTime End)>();
        foreach (var track in targets.GroupBy(line => line.TrackId))
        {
            var lines = track.OrderBy(line => line.Start).ToArray();
            AddLeads(lines, options);
            if (options.AdjacencyEnabled)
            {
                MakeAdjacent(lines, options);
            }
            if (options.KeyframeSnapEnabled)
            {
                SnapKeyframes(lines, options, video!);
            }

            foreach (var line in lines)
            {
                timings.Add(line.Id, (line.Start, line.End));
            }
        }

        return ProjectEditingOperations.SetSubtitleTimings(document, timings);
    }

    private static void AddLeads(SubtitleLine[] lines, TimingPostProcessorOptions options)
    {
        if (options.LeadInEnabled)
        {
            var leadIn = new MediaTime(options.LeadInMilliseconds, 1000);
            for (var index = 0; index < lines.Length; index++)
            {
                var start = lines[index].Start - leadIn;
                start = start < MediaTime.Zero ? MediaTime.Zero : start;
                if (index > 0 && start < lines[index - 1].End)
                {
                    start = lines[index - 1].End;
                }
                lines[index] = lines[index] with { Start = start };
            }
        }

        if (options.LeadOutEnabled)
        {
            var leadOut = new MediaTime(options.LeadOutMilliseconds, 1000);
            for (var index = 0; index < lines.Length; index++)
            {
                var end = lines[index].End + leadOut;
                if (index + 1 < lines.Length && end > lines[index + 1].Start)
                {
                    end = lines[index + 1].Start;
                }
                lines[index] = lines[index] with { End = end };
            }
        }
    }

    private static void MakeAdjacent(SubtitleLine[] lines, TimingPostProcessorOptions options)
    {
        var maximumGap = new MediaTime(options.MaximumGapMilliseconds, 1000);
        var maximumOverlap = new MediaTime(options.MaximumOverlapMilliseconds, 1000);
        for (var index = 1; index < lines.Length; index++)
        {
            var previous = lines[index - 1];
            var current = lines[index];
            var distance = current.Start - previous.End;
            if (distance > MediaTime.Zero && distance <= maximumGap ||
                distance < MediaTime.Zero && -distance <= maximumOverlap)
            {
                var boundary = previous.End + distance * options.BiasPercent / 100;
                lines[index - 1] = previous with { End = boundary };
                lines[index] = current with { Start = boundary };
            }
        }
    }

    private static void SnapKeyframes(SubtitleLine[] lines, TimingPostProcessorOptions options, VideoTimingIndex video)
    {
        var startBefore = new MediaTime(options.StartBeforeMilliseconds, 1000);
        var startAfter = new MediaTime(options.StartAfterMilliseconds, 1000);
        var endBefore = new MediaTime(options.EndBeforeMilliseconds, 1000);
        var endAfter = new MediaTime(options.EndAfterMilliseconds, 1000);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var startKeyframe = video.NearestKeyframe(video.FrameAtTime(line.Start));
            var endKeyframe = video.NearestKeyframe(video.FrameAtTime(line.End, end: true));
            var startTarget = video.KeyframeBoundary(startKeyframe);
            var endTarget = video.KeyframeBoundary(endKeyframe);
            var start = WithinWindow(line.Start, startTarget, startBefore, startAfter) ? startTarget : line.Start;
            var end = WithinWindow(line.End, endTarget, endBefore, endAfter) ? endTarget : line.End;
            lines[index] = line with { Start = start, End = end };
        }
    }

    private static bool WithinWindow(MediaTime time, MediaTime target, MediaTime before, MediaTime after)
    {
        return time < target ? time >= target - before : time <= target + after;
    }
}
