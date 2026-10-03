using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private static readonly TextAlignment[] alignments =
    [
        TextAlignment.BOTTOM_CENTER, TextAlignment.TOP_CENTER, TextAlignment.MIDDLE_CENTER, TextAlignment.BOTTOM_LEFT,
        TextAlignment.BOTTOM_RIGHT, TextAlignment.TOP_LEFT, TextAlignment.TOP_RIGHT, TextAlignment.MIDDLE_LEFT,
        TextAlignment.MIDDLE_RIGHT
    ];

    private AnimationProperty ActiveProperty => layerEditing.ActiveProperty;
    private Keyframe? SelectedKeyframe => layerEditing.SelectedKeyframe;
    internal void ClearKeyframeSelection() => layerEditing.ClearKeyframeSelection();
    internal void RefreshKeyframeInspector() => layerEditing.RefreshKeyframeInspector();
    internal void AddKeyframe() => layerEditing.AddKeyframe();
    internal void DeleteKeyframe() => layerEditing.DeleteKeyframe();
    internal void CreateKaraoke() => layerEditing.CreateKaraoke();
    internal void SavePreset() => layerEditing.SavePreset();
    internal void AddRectangle() => layerEditing.AddRectangle();
    internal void AddEllipse() => layerEditing.AddEllipse();
    internal void DeleteLayer() => layerEditing.DeleteLayer();
    internal void GroupLayers() => layerEditing.GroupLayers();
    internal void UngroupLayer() => layerEditing.UngroupLayer();
    internal void MoveLayerUp() => layerEditing.MoveLayerUp();
    internal void MoveLayerDown() => layerEditing.MoveLayerDown();
    internal void EditPath() => layerEditing.EditPath();
    internal void EditMask() => layerEditing.EditMask();
    internal void ClearPath() => layerEditing.ClearPath();
    internal void ClearMask() => layerEditing.ClearMask();
    internal void ApplyFade() => layerEditing.ApplyFade();
    internal void ApplyPop() => layerEditing.ApplyPop();
    internal void ApplySlide() => layerEditing.ApplySlide();
    internal void ApplySelectedPreset() => layerEditing.ApplySelectedPreset();
    internal void ClearKaraoke() => layerEditing.ClearKaraoke();
    internal Task ImportFontAsync() => layerEditing.ImportFontAsync();
    internal Task ImportImageAsync() => layerEditing.ImportImageAsync();
    internal void SelectKeyframe(TimelineKeyframeEventArgs value) => layerEditing.SelectKeyframe(value);
    internal void MoveKeyframe(TimelineKeyframeEventArgs value) => layerEditing.MoveKeyframe(value);
}
