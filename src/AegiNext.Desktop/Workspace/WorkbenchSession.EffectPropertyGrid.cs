using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal event EventHandler? NumericGestureCancellationRequested;

    internal void NotifyEffectPropertyDraftChanged()
    {
        draftRevision++;
        NotifyTaskInputChanged();
    }

    internal void ClearEffectPropertyDraftError(string field)
    {
        if (ViewModel.InvalidPanelId == "effects" && ViewModel.InvalidFieldKey == field)
        {
            ViewModel.InvalidPanelId = null;
            ViewModel.InvalidFieldKey = null;
            ViewModel.Effects.InvalidFieldKey = null;
            ViewModel.Effects.ValidationError = null;
        }
    }

    internal void RefreshEffectsInspectorTarget()
    {
        using var update = BeginWorkbenchUpdate();
        RefreshInspector();
    }

    internal AnimationValue EffectPropertyValue(ProjectLayer layer, AnimationTrackTarget target)
    {
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.FirstOrDefault(line => line.Id == id) : null;
        var track = layer.Tracks.FirstOrDefault(track => track.Target == target);
        return track is not null && AnimationTarget is { } edit
            ? SceneEvaluator.EvaluateTrack(track, edit.LocalTime)
            : SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target);
    }

    internal Task CreateSelectedTextAnimationRangeAsync() => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedCue is not { } subtitle || Details.TextSelectionLength <= 0)
        {
            return;
        }
        var start = Details.TextSelectionStart;
        var length = Details.TextSelectionLength;
        var boundaries = new SubtitleTextBoundaries(subtitle.Text);
        if (!boundaries.Contains(start) || !boundaries.Contains(start + length))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.AnimationRangeGraphemeSelection"));
        }
        var range = new SubtitleAnimationRange(Guid.NewGuid(), start, length);
        editor.SetSubtitleAnimationRange(subtitle.Id, range);
        ViewModel.Effects.SelectedScope = ViewModel.Effects.Scopes.Single(scope => scope.Id == range.Id);
    }));

    internal Task DeleteSelectedTextAnimationRangeAsync() => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedCue is { } subtitle && SceneEditing.Target.TextRangeId is { } id)
        {
            editor.RemoveSubtitleAnimationRange(subtitle.Id, id);
            ViewModel.Effects.Target = SceneEditing.Target with { TextRangeId = null };
        }
    }));

    internal Task MoveSelectedTextAnimationRangeAsync(int direction) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedCue is not { } subtitle || SceneEditing.Target.TextRangeId is not { } id)
        {
            return;
        }
        var index = Array.FindIndex(subtitle.AnimationRanges.ToArray(), range => range.Id == id);
        var next = index + direction;
        if (index >= 0 && next >= 0 && next < subtitle.AnimationRanges.Length)
        {
            editor.MoveSubtitleAnimationRange(subtitle.Id, id, next);
        }
    }));

    internal Task ToggleEffectPropertyAnimationAsync(AnimationProperty property)
    {
        var target = SceneEditing.Target with { Property = property, NodeId = null };
        var enabled = SelectedLayer?.Tracks.Any(track => track.Target == target) != true;
        return SetEffectPropertyAnimationAsync(target, enabled);
    }

    internal Task SetEffectPropertyAnimationAsync(AnimationTrackTarget target, bool enabled) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedLayer is not { } layer)
        {
            return;
        }
        if (!enabled)
        {
            editor.ClearAnimationTracks([layer.Id], target);
            ClearKeyframeSelection();
        }
        else if (!layer.Tracks.Any(track => track.Target == target))
        {
            AddEffectPropertyKeyframe(target);
        }
        ViewModel.Effects.Target = target;
    }));

    internal Task AddEffectPropertyKeyframeAsync(AnimationTrackTarget target) => RunCommandAsync(() => EditAsync(() => AddEffectPropertyKeyframe(target)));

    internal bool CanAddEffectPropertyKeyframe(Guid layerId)
    {
        if (SelectedLayer is not { } layer || layer.Id != layerId)
        {
            return false;
        }
        var time = SelectedKeyTime ?? ProjectPosition - layer.Start + layer.AnimationOffset;
        return LayerAnimationTiming.ClampTime(layer, time) == time;
    }

    private void AddEffectPropertyKeyframe(AnimationTrackTarget target)
    {
        if (SelectedLayer is not { } layer || AnimationTarget is not { } edit || !CanAddEffectPropertyKeyframe(layer.Id) ||
            layer.Tracks.FirstOrDefault(track => track.Target == target)?.Transforms.IsEmpty == false)
        {
            return;
        }
        var value = EffectPropertyValue(layer, target);
        editor.SetKeyframe(layer.Id, target, new(edit.LocalTime, value));
        SelectKeyframe(new(layer.Id, target, edit.LocalTime, edit.LocalTime));
    }

    internal Task SetEffectColorSpaceAsync(AnimationTrackTarget target, AnimationColorSpace space) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedLayer is not { } layer)
        {
            return;
        }
        editor.UpdateLayer(layer.Id, current => current with
        {
            Tracks = current.Tracks.Select(track => track.Target == target ? track with { ColorSpace = space } : track).ToImmutableArray()
        });
    }));

    internal Task ResetEffectPropertyAsync(AnimationTrackTarget target) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedLayer is not { } layer || AnimationTarget is not { } edit)
        {
            return;
        }
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        var value = SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target);
        var prepared = AnimationEditOperations.SetValue(editor.Snapshot, edit, target, value);
        if (prepared != editor.Snapshot)
        {
            editor.Apply("Reset animated property to base value", _ => prepared);
        }
    }));
}
