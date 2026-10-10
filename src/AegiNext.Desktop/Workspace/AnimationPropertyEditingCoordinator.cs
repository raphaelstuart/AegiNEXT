using AegiNext.Core.Projects;
using AegiNext.Core.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed class AnimationPropertyEditingCoordinator(WorkbenchSession session)
{
    private readonly Dictionary<(Guid LayerId, AnimationTrackTarget Target), AnimationPropertyRowViewModel> rows = [];

    internal bool HasDrafts => rows.Values.Any(row => row.HasDraft);

    internal AnimationPropertyRowViewModel GetRow(Guid layerId, AnimationTrackTarget target)
    {
        var layer = session.DocumentSnapshot.Layers.Single(layer => layer.Id == layerId);
        if (!rows.TryGetValue((layerId, target), out var row))
        {
            row = new(session, layerId, target);
            rows.Add((layerId, target), row);
        }
        var subtitle = layer.SubtitleId is { } id ? session.DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        row.Load(session.EffectPropertyValue(layer, target), layer.Tracks.FirstOrDefault(track => track.Target == target),
            !SubtitleAnimationEvaluation.IsBaseValueUniform(layer, subtitle, target));
        return row;
    }

    internal AnimationPropertyRowViewModel? FindRow(string? field) => rows.Values.FirstOrDefault(row => row.OwnsField(field));

    internal void Refresh()
    {
        foreach (var key in rows.Keys.Where(key =>
        {
            var layer = session.DocumentSnapshot.Layers.FirstOrDefault(layer => layer.Id == key.LayerId);
            return layer is null || key.Target.TextRangeId is { } rangeId &&
                !session.DocumentSnapshot.Subtitles.Any(line => line.Id == layer.SubtitleId && line.AnimationRanges.Any(range => range.Id == rangeId)) ||
                AnimationPropertyMetadata.IsMaskProperty(key.Target.Property) && (layer.Mask is null ||
                    key.Target.NodeId is { } nodeId && (layer.Mask is not VectorClipMask vector ||
                        !vector.Contours.SelectMany(contour => contour.Nodes).Any(node => node.Id == nodeId)));
        }).ToArray())
        {
            rows.Remove(key);
        }
    }

    internal ProjectDocument Prepare(ProjectDocument document)
    {
        foreach (var row in rows.Values.Where(row => row.HasDraft))
        {
            document = row.Prepare(document);
        }
        return document;
    }

    internal ProjectDocument Overlay(ProjectDocument document)
    {
        var invalidPanel = session.ViewModel.InvalidPanelId;
        var invalidField = session.ViewModel.InvalidFieldKey;
        try
        {
            foreach (var row in rows.Values.Where(row => row.HasDraft))
            {
                try
                {
                    var candidate = row.Prepare(document);
                    ProjectValidator.Validate(candidate);
                    document = candidate;
                }
                catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException)
                {
                }
            }
        }
        finally
        {
            session.ViewModel.InvalidPanelId = invalidPanel;
            session.ViewModel.InvalidFieldKey = invalidField;
        }
        return document;
    }

    internal void Accept()
    {
        foreach (var row in rows.Values.Where(row => row.HasDraft))
        {
            row.Accept();
        }
        Refresh();
    }

    internal bool Restore(string? field)
    {
        if (field is null || FindRow(field) is not { } row)
        {
            return false;
        }
        row.Restore(field);
        return true;
    }
}
