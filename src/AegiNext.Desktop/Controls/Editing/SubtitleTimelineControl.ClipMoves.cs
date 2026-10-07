using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private readonly Dictionary<Guid, ProjectLayer> movingClips = [];

    private bool IsBatchMove => dragMode == TimelineDragMode.MOVE && movingClips.Count > 1;

    private MediaTime ClipDeltaFromSeconds(double seconds, KeyModifiers modifiers)
    {
        var denominator = IsStepEnabled && (modifiers & KeyModifiers.Alt) == 0 ? 1000000 : 1000;
        return new((long)Math.Round(seconds * denominator), denominator);
    }

    private void FreezeMovingClips(Guid id, TimelineDragMode mode)
    {
        if (mode != TimelineDragMode.MOVE)
        {
            return;
        }

        foreach (var layer in layersById.Values.Where(layer => layer.Kind != LayerKind.GROUP &&
                     (layer.Id == id || selectedIds.Contains(id) && selectedIds.Contains(layer.Id))))
        {
            movingClips.Add(layer.Id, layer);
        }
    }

    private void UpdateMove(MediaTime delta, KeyModifiers modifiers)
    {
        var earliest = movingClips.Count == 0 ? originalStart : movingClips.Values.Min(layer => layer.Start);
        var offset = delta == MediaTime.Zero ? MediaTime.Zero :
            Max(-earliest, QuantizeEdit(originalStart + delta, modifiers) - originalStart);
        pendingStart = originalStart + offset;
        pendingEnd = originalEnd + offset;
        if (delta != MediaTime.Zero && ShouldSnap(modifiers))
        {
            var snap = TimelineQuantization.ResolveSnapOffset(pendingStart, pendingEnd, snapBoundaries, PixelsPerSecond, position);
            offset = Max(-earliest, offset + snap.Value);
            pendingStart = originalStart + offset;
            pendingEnd = originalEnd + offset;
            snapTarget = snap.Boundary == pendingStart || snap.Boundary == pendingEnd ? snap.Boundary : null;
        }
    }

    private bool BatchDropIsValid()
    {
        var offset = pendingStart - originalStart;
        var movedSubtitleIds = movingClips.Values.Where(layer => layer.SubtitleId.HasValue)
            .Select(layer => layer.SubtitleId!.Value).ToHashSet();
        var others = document.Subtitles.Where(cue => !movedSubtitleIds.Contains(cue.Id)).ToArray();
        return movingClips.Values.Where(layer => layer.SubtitleId.HasValue).All(layer =>
        {
            var cue = cuesById[layer.SubtitleId!.Value];
            return !others.Any(other => other.TrackId == cue.TrackId &&
                cue.Start + offset < other.End && other.Start < cue.End + offset);
        });
    }

    private ProjectLayer? ContextClipAt(Point point, TimelineRow row)
    {
        if (FindKeyframe(point) is { } marker)
        {
            return layersById[marker.Identity.LayerId];
        }

        var clip = row.Clips.Reverse().FirstOrDefault(layer => ClipRectangle(layer, row).Contains(point));
        if (clip is not null || point.Y >= RowY(row) + row.CurveHeight)
        {
            return clip;
        }

        var time = TimeAt(point.X, KeyModifiers.Alt);
        return row.Clips.Reverse().FirstOrDefault(layer => layer.Start <= time && time < layer.End);
    }

    private void RequestClipContext(Point point, KeyModifiers modifiers)
    {
        var target = ClipPasteTargetAt(point, modifiers);
        if (target is null)
        {
            return;
        }

        var clip = target.LayerId is { } id ? layersById[id] : null;
        if (clip is not null && !selectedIds.Contains(clip.Id) && !SelectClip(clip, KeyModifiers.None))
        {
            return;
        }

        ClipContextRequested?.Invoke(this, target);
    }
}
