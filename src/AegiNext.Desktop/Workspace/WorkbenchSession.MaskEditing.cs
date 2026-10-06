using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal AnimationPropertyChoice[] MaskPropertyChoices()
    {
        return AnimationPropertyMetadata.CurrentProperties.Where(property =>
                !AnimationPropertyMetadata.IsMaskProperty(property) || SelectedLayer?.Mask is not null &&
                (!AnimationPropertyMetadata.IsNodeProperty(property) || SceneEditing.MaskNodeId is not null) &&
                (SelectedLayer.Mask is RectangleClipMask || property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)))
            .Select(property => new AnimationPropertyChoice(new AnimationTrackTarget(property,
                    AnimationPropertyMetadata.IsNodeProperty(property) ? SceneEditing.MaskNodeId : null), AnimationPropertyLocalization.Get(property)))
            .ToArray();
    }
}
