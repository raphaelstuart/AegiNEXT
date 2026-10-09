using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal Task MoveSubtitleSelectionAsync(IReadOnlyCollection<Guid> subtitleIds, ProjectDocument? expected = null)
    {
        var targets = subtitleIds.ToHashSet();
        return RunCommandAsync(() =>
        {
            if (IsUpdating || !TimelineContextIsCurrent(expected) || targets.Count == 0)
            {
                return Task.CompletedTask;
            }

            var source = editor.Snapshot;
            if (source.Subtitles.Count(line => targets.Contains(line.Id)) != targets.Count)
            {
                throw new KeyNotFoundException(Localization.Get("Workbench.NoSelection"));
            }

            var layerIds = source.Layers
                .Where(layer => layer.SubtitleId is { } id && targets.Contains(id))
                .Select(layer => layer.Id).ToArray();
            return ShowClipMovementAsync(layerIds, source);
        });
    }

    internal Task MoveTimelineClipsAsync(IReadOnlyCollection<Guid> layerIds, ProjectDocument? expected = null)
    {
        var targets = layerIds.Distinct().ToArray();
        return RunCommandAsync(() => IsUpdating || !TimelineContextIsCurrent(expected) || targets.Length == 0
            ? Task.CompletedTask : ShowClipMovementAsync(targets, editor.Snapshot));
    }

    private async Task ShowClipMovementAsync(Guid[] layerIds, ProjectDocument source)
    {
        var generation = projectGeneration;
        int? milliseconds;
        {
            using var editingLease = AcquireEditingLease();
            milliseconds = await dialogs.ShowIntegerInputAsync(new("Workbench.Move", "Workbench.MoveMilliseconds",
                "Workbench.MoveMillisecondsHint"), ProjectOperationsToken);
        }

        if (milliseconds is null or 0 || generation != projectGeneration || !TimelineContextIsCurrent(source))
        {
            return;
        }

        await EditAsync(() =>
        {
            try
            {
                editor.ShiftClips(layerIds, new MediaTime(milliseconds.Value, 1000));
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or
                KeyNotFoundException or OverflowException)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.MoveFailed"), error);
            }
        });
    }
}
