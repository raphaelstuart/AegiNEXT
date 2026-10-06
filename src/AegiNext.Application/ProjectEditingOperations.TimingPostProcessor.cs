using AegiNext.Application.Timing;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>Updates subtitle rows and nested layers in one snapshot using crop semantics; collisions reject the complete batch.</summary>
    public static ProjectDocument SetSubtitleTimings(ProjectDocument document,
        IReadOnlyDictionary<Guid, (MediaTime Start, MediaTime End)> timings)
    {
        ArgumentNullException.ThrowIfNull(timings);
        ProjectValidator.Validate(document);
        var existingIds = document.Subtitles.Select(line => line.Id).ToHashSet();
        if (timings.Keys.Any(id => !existingIds.Contains(id)))
        {
            throw new TimingPostProcessorException("A subtitle in the timing batch no longer exists.");
        }
        if (timings.Values.Any(value => value.Start >= value.End))
        {
            throw new TimingPostProcessorException("Timing processing must leave every subtitle with a positive duration.");
        }

        var subtitles = document.Subtitles.ToBuilder();
        var changed = new Dictionary<Guid, (MediaTime Start, MediaTime End)>();
        for (var index = 0; index < subtitles.Count; index++)
        {
            var line = subtitles[index];
            if (timings.TryGetValue(line.Id, out var timing) && (line.Start != timing.Start || line.End != timing.End))
            {
                subtitles[index] = line with { Start = timing.Start, End = timing.End };
                changed.Add(line.Id, timing);
            }
        }
        if (changed.Count == 0)
        {
            return document;
        }

        var result = document with
        {
            Subtitles = subtitles.ToImmutable(),
            Layers = MapTrackLayers(document.Layers, layer => layer.SubtitleId is { } id && changed.TryGetValue(id, out var timing)
                ? LayerAnimationTiming.Retime(layer, timing.Start, timing.End, TimelineEditMode.CROP) : layer)
        };
        try
        {
            ProjectValidator.Validate(result);
        }
        catch (InvalidDataException error)
        {
            throw new TimingPostProcessorException("Timing processing would create a subtitle collision or an invalid project. The complete batch was rejected.", error);
        }
        return result;
    }
}
