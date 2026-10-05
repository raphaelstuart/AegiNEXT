using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool HasEffectDrafts => effectsDirty;
    private long draftRevision;
    private (long Revision, string? Panel, string? Field, string Message)? lastDraftDiagnostic;
    private (long Revision, string? Panel, string? Field)? lastDraftFocus;

    private void ReportDraftError(Exception error, bool focusInvalid)
    {
        var diagnostic = (draftRevision, ViewModel.InvalidPanelId, ViewModel.InvalidFieldKey, error.Message);
        if (lastDraftDiagnostic != diagnostic)
        {
            lastDraftDiagnostic = diagnostic;
            ShowError(error);
        }
        if (ViewModel.InvalidPanelId == "effects")
        {
            ViewModel.Effects.InvalidFieldKey = ViewModel.InvalidFieldKey;
            ViewModel.Effects.ValidationError = error.Message;
        }
        var focus = (draftRevision, ViewModel.InvalidPanelId, ViewModel.InvalidFieldKey);
        if (focusInvalid && lastDraftFocus != focus)
        {
            lastDraftFocus = focus;
            ViewModel.FocusDraftError();
        }
    }

    private double ReadEffectNumber(string text, double original, string label, string field, string property) =>
        changedEffectFields.Contains(property) ? ReadNumber(text, original, label, field) : original;

    internal void RestoreEffectDraftField(string fieldKey)
    {
        if (SelectedLayer is not { } layer || updatingWorkbench)
        {
            return;
        }
        var vm = ViewModel.Effects;
        var target = AnimationTarget!;
        var placement = ResolvePlacement(editor.Snapshot, layer);
        var propertyName = fieldKey.EndsWith("Input", StringComparison.Ordinal) ? fieldKey[..^5] : fieldKey;
        updatingWorkbench = true;
        try
        {
            switch (propertyName)
            {
                case "LayerStart": vm.LayerStart = TimelineTimeText.Format(layer.Start); break;
                case "LayerEnd": vm.LayerEnd = TimelineTimeText.Format(layer.End); break;
                case "LayerWidth": vm.LayerWidth = (decimal)(layer.Shape?.Width ?? layer.Image?.Width ?? 300); break;
                case "LayerHeight": vm.LayerHeight = (decimal)(layer.Shape?.Height ?? layer.Image?.Height ?? 180); break;
                case "PositionX": vm.PositionX = placement.BasePosition is { } x ? (decimal)(x.X + InspectorVector(layer, AnimationProperty.POSITION, layer.Transform.Position).X) : null; break;
                case "PositionY": vm.PositionY = placement.BasePosition is { } y ? (decimal)(y.Y + InspectorVector(layer, AnimationProperty.POSITION, layer.Transform.Position).Y) : null; break;
                case "ScaleX": vm.ScaleX = (decimal)InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale).X; break;
                case "ScaleY": vm.ScaleY = (decimal)InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale).Y; break;
                case "Rotation": vm.Rotation = (decimal)InspectorValue(layer, AnimationProperty.ROTATION, layer.Transform.Rotation); break;
                case "Opacity": vm.Opacity = (decimal)InspectorValue(layer, AnimationProperty.OPACITY, layer.Opacity); break;
                case "Blur": vm.Blur = (decimal)InspectorValue(layer, AnimationProperty.BLUR, layer.Blur); break;
                case "KeyframeValueX":
                case "KeyframeValueY":
                case "KeyframeValue":
                    var frame = layer.Tracks.FirstOrDefault(track => track.Property == (target.Property ?? ActiveProperty))?.Keyframes.FirstOrDefault(key => key.Time == target.LocalTime);
                    if (propertyName == "KeyframeValueY")
                    {
                        vm.KeyframeValueY = frame?.Value.IsVector == true ? (decimal)frame.Value.Vector.Y : vm.KeyframeValueY ?? 0;
                        vm.KeyframeValueYText = vm.KeyframeValueY.Value.ToString(InterfaceCulture);
                    }
                    else
                    {
                        vm.KeyframeValue = frame is null ? vm.KeyframeValue ?? 0 : (decimal)frame.Value.GetComponent(0);
                        vm.KeyframeValueText = vm.KeyframeValue.Value.ToString(InterfaceCulture);
                        propertyName = "KeyframeValue";
                    }
                    break;
                default: return;
            }
            switch (propertyName)
            {
                case "LayerWidth": vm.LayerWidthText = vm.LayerWidth?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "LayerHeight": vm.LayerHeightText = vm.LayerHeight?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "PositionX": vm.PositionXText = vm.PositionX?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "PositionY": vm.PositionYText = vm.PositionY?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "ScaleX": vm.ScaleXText = vm.ScaleX?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "ScaleY": vm.ScaleYText = vm.ScaleY?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "Rotation": vm.RotationText = vm.Rotation?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "Opacity": vm.OpacityText = vm.Opacity?.ToString(InterfaceCulture) ?? string.Empty; break;
                case "Blur": vm.BlurText = vm.Blur?.ToString(InterfaceCulture) ?? string.Empty; break;
            }
            changedEffectFields.Remove(propertyName);
            changedEffectFields.Remove(propertyName + "Text");
            effectsDirty = changedEffectFields.Count > 0;
            if (!effectsDirty && !stylesDirty)
            {
                SceneEditing.DraftTarget = null;
            }
            draftRevision++;
            if (vm.InvalidFieldKey == fieldKey)
            {
                vm.InvalidFieldKey = null;
                vm.ValidationError = null;
                ViewModel.InvalidPanelId = null;
                ViewModel.InvalidFieldKey = null;
            }
        }
        finally
        {
            updatingWorkbench = false;
        }
    }
}
