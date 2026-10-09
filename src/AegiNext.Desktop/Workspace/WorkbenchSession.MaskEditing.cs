using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ClipMask? propertyChoicesMask;
    private string? propertyChoicesLanguage;
    private LayerKind? propertyChoicesLayerKind;
    private AnimationPropertyChoice[] maskPropertyChoices = [];

    internal AnimationPropertyChoice[] MaskPropertyChoices()
    {
        var mask = SelectedLayer?.Mask;
        var kind = SelectedLayer?.Kind;
        if (propertyChoicesLanguage == Localization.CurrentLanguageID && ReferenceEquals(propertyChoicesMask, mask) && propertyChoicesLayerKind == kind)
        {
            return maskPropertyChoices;
        }
        var choices = AnimationPropertyMetadata.CurrentProperties
            .Where(property => !AnimationPropertyMetadata.IsSubtitleOnlyProperty(property) || kind == LayerKind.SUBTITLE)
            .Where(property =>
                !AnimationPropertyMetadata.IsMaskProperty(property) || mask is not null &&
                (!AnimationPropertyMetadata.IsNodeProperty(property) || mask is VectorClipMask) &&
                (mask is RectangleClipMask || property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)))
            .Select(property => new AnimationPropertyChoice(property, AnimationPropertyLocalization.Get(property))).ToArray();
        propertyChoicesMask = mask;
        propertyChoicesLayerKind = kind;
        propertyChoicesLanguage = Localization.CurrentLanguageID;
        maskPropertyChoices = choices;
        return maskPropertyChoices;
    }
}
