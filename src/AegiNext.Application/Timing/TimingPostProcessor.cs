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

        var clips = new ProjectClipIndex(document);
        var timings = new Dictionary<Guid, (MediaTime Start, MediaTime End)>();
        foreach (var track in targets.GroupBy(line => clips.GetSubtitleTrackId(line.Id)))
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

    /// <summary>Processes per-subtitle configurations across complete tracks, retaining unselected boundaries and validating one final crop batch.</summary>
    /// <param name="document">The committed project snapshot.</param>
    /// <param name="optionsBySubtitle">The complete configuration associated with each target subtitle ID.</param>
    /// <param name="video">The actual video frame and keyframe timing index.</param>
    /// <param name="skipUnavailableKeyframes">Skips only keyframe snapping when the timing index is unavailable, retaining complete configuration equality for adjacency.</param>
    public static ProjectDocument Process(ProjectDocument document,
        IReadOnlyDictionary<Guid, TimingPostProcessorOptions> optionsBySubtitle, VideoTimingIndex? video = null,
        bool skipUnavailableKeyframes = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(optionsBySubtitle);
        ProjectValidator.Validate(document);
        var configurations = optionsBySubtitle.ToDictionary(pair => pair.Key, pair => pair.Value);
        var existingIds = document.Subtitles.Select(line => line.Id).ToHashSet();
        foreach (var (id, options) in configurations)
        {
            if (!existingIds.Contains(id))
            {
                throw new TimingPostProcessorException("A subtitle in the timing batch no longer exists.");
            }

            ArgumentNullException.ThrowIfNull(options);
            options.Validate();
        }

        if (!configurations.Values.Any(options => options.LeadInEnabled || options.LeadOutEnabled ||
            options.AdjacencyEnabled || options.KeyframeSnapEnabled))
        {
            return document;
        }

        var keyframesAvailable = video is not null && !video.Keyframes.IsEmpty;
        if (configurations.Values.Any(options => options.KeyframeSnapEnabled) && !keyframesAvailable && !skipUnavailableKeyframes)
        {
            throw new TimingPostProcessorException("Keyframe snapping requires an available video timing index.");
        }

        var clips = new ProjectClipIndex(document);
        var tracks = document.Subtitles.GroupBy(line => clips.GetSubtitleTrackId(line.Id))
            .Where(track => track.Any(line => configurations.ContainsKey(line.Id)))
            .Select(track => track.OrderBy(line => line.Start).ToArray()).ToArray();
        foreach (var lines in tracks)
        {
            AddLeadIns(lines, configurations);
        }
        foreach (var lines in tracks)
        {
            AddLeadOuts(lines, configurations);
        }
        foreach (var lines in tracks)
        {
            MakeAdjacent(lines, configurations);
        }
        if (keyframesAvailable)
        {
            foreach (var lines in tracks)
            {
                SnapKeyframes(lines, configurations, video!);
            }
        }

        var timings = new Dictionary<Guid, (MediaTime Start, MediaTime End)>(configurations.Count);
        foreach (var lines in tracks)
        {
            foreach (var line in lines)
            {
                if (configurations.ContainsKey(line.Id))
                {
                    timings.Add(line.Id, (line.Start, line.End));
                }
            }
        }

        return ProjectEditingOperations.SetSubtitleTimings(document, timings);
    }

    private static void AddLeadIns(SubtitleLine[] lines, Dictionary<Guid, TimingPostProcessorOptions> configurations)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!configurations.TryGetValue(line.Id, out var options) || !options.LeadInEnabled)
            {
                continue;
            }

            var start = line.Start - new MediaTime(options.LeadInMilliseconds, 1000);
            start = start < MediaTime.Zero ? MediaTime.Zero : start;
            if (index > 0 && start < lines[index - 1].End)
            {
                start = lines[index - 1].End;
            }

            lines[index] = line with { Start = start };
        }
    }

    private static void AddLeadOuts(SubtitleLine[] lines, Dictionary<Guid, TimingPostProcessorOptions> configurations)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!configurations.TryGetValue(line.Id, out var options) || !options.LeadOutEnabled)
            {
                continue;
            }

            var end = line.End + new MediaTime(options.LeadOutMilliseconds, 1000);
            if (index + 1 < lines.Length && end > lines[index + 1].Start)
            {
                end = lines[index + 1].Start;
            }

            lines[index] = line with { End = end };
        }
    }

    private static void MakeAdjacent(SubtitleLine[] lines, Dictionary<Guid, TimingPostProcessorOptions> configurations)
    {
        for (var index = 1; index < lines.Length; index++)
        {
            var previous = lines[index - 1];
            var current = lines[index];
            if (!configurations.TryGetValue(previous.Id, out var options) || !options.AdjacencyEnabled ||
                !configurations.TryGetValue(current.Id, out var currentOptions) || options != currentOptions)
            {
                continue;
            }

            var maximumGap = new MediaTime(options.MaximumGapMilliseconds, 1000);
            var maximumOverlap = new MediaTime(options.MaximumOverlapMilliseconds, 1000);
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

    private static void SnapKeyframes(SubtitleLine[] lines, Dictionary<Guid, TimingPostProcessorOptions> configurations,
        VideoTimingIndex video)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (configurations.TryGetValue(lines[index].Id, out var options) && options.KeyframeSnapEnabled)
            {
                lines[index] = SnapKeyframe(lines[index], options, video);
            }
        }
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
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = SnapKeyframe(lines[index], options, video);
        }
    }

    private static SubtitleLine SnapKeyframe(SubtitleLine line, TimingPostProcessorOptions options, VideoTimingIndex video)
    {
        var startBefore = new MediaTime(options.StartBeforeMilliseconds, 1000);
        var startAfter = new MediaTime(options.StartAfterMilliseconds, 1000);
        var endBefore = new MediaTime(options.EndBeforeMilliseconds, 1000);
        var endAfter = new MediaTime(options.EndAfterMilliseconds, 1000);
        var startKeyframe = video.NearestKeyframe(video.FrameAtTime(line.Start));
        var endKeyframe = video.NearestKeyframe(video.FrameAtTime(line.End, end: true));
        var startTarget = video.KeyframeBoundary(startKeyframe);
        var endTarget = video.KeyframeBoundary(endKeyframe);
        var start = WithinWindow(line.Start, startTarget, startBefore, startAfter) ? startTarget : line.Start;
        var end = WithinWindow(line.End, endTarget, endBefore, endAfter) ? endTarget : line.End;
        return line with { Start = start, End = end };
    }

    private static bool WithinWindow(MediaTime time, MediaTime target, MediaTime before, MediaTime after)
    {
        return time < target ? time >= target - before : time <= target + after;
    }
}
