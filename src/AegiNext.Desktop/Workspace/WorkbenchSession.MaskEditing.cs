using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ClipMask? propertyChoicesMask;
    private string? propertyChoicesLanguage;
    private LayerKind? propertyChoicesLayerKind;
    private Guid? propertyChoicesRange;
    private SubtitleAnimationState propertyChoicesState;
    private AnimationPropertyChoice[] maskPropertyChoices = [];

    internal AnimationPropertyChoice[] MaskPropertyChoices()
    {
        var mask = SelectedLayer?.Mask;
        var kind = SelectedLayer?.Kind;
        if (propertyChoicesLanguage == Localization.CurrentLanguageID && ReferenceEquals(propertyChoicesMask, mask) && propertyChoicesLayerKind == kind && propertyChoicesRange == SceneEditing.Target.TextRangeId &&
            propertyChoicesState == SceneEditing.Target.State)
        {
            return maskPropertyChoices;
        }
        var choices = AnimationPropertyMetadata.CurrentProperties
            .Where(property => SceneEditing.Target.TextRangeId is null || Panels.Effects.EffectsPanelViewModel.IsRangeProperty(property))
            .Where(property => SceneEditing.Target.State == SubtitleAnimationState.NORMAL || Panels.Effects.EffectsPanelViewModel.IsAppearanceProperty(property))
            .Where(property => !AnimationPropertyMetadata.IsSubtitleOnlyProperty(property) || kind == LayerKind.SUBTITLE)
            .Where(property =>
                !AnimationPropertyMetadata.IsMaskProperty(property) || mask is not null &&
                (!AnimationPropertyMetadata.IsNodeProperty(property) || mask is VectorClipMask) &&
                (mask is RectangleClipMask || property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)))
            .Select(property => new AnimationPropertyChoice(property, AnimationPropertyLocalization.Get(property))).ToArray();
        propertyChoicesMask = mask;
        propertyChoicesLayerKind = kind;
        propertyChoicesRange = SceneEditing.Target.TextRangeId;
        propertyChoicesState = SceneEditing.Target.State;
        propertyChoicesLanguage = Localization.CurrentLanguageID;
        maskPropertyChoices = choices;
        return maskPropertyChoices;
    }
}
