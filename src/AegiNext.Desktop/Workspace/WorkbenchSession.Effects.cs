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
    internal void ResetAutomaticPosition() => layerEditing.ResetAutomaticPosition();
    internal void AddPathPoint() => layerEditing.AddPathPoint();
    internal void RemovePathPoint() => layerEditing.RemovePathPoint();
    internal void EditPath() => layerEditing.EditPath();
    internal void ClearPath() => layerEditing.ClearPath();
    internal void ApplySelectedPreset() => layerEditing.ApplySelectedPreset();
    internal void ClearKaraoke() => layerEditing.ClearKaraoke();
    internal Task ImportFontAsync() => layerEditing.ImportFontAsync();
    internal bool SelectKeyframe(TimelineKeyframeEventArgs value) => layerEditing.SelectKeyframe(value);
    internal void MoveKeyframe(TimelineKeyframeEventArgs value) => layerEditing.MoveKeyframe(value);
}
