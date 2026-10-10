using System.Windows.Input;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private AnimationPropertyRowViewModel[] maskRows = [];

    public bool MaskExpanded { get => IsExpanded("mask"); set => SetExpanded("mask", value); }
    public bool CanEditMask => session.MaskEditing.CanEdit;
    public bool HasMask => session.SelectedLayer?.Mask is not null;
    public bool NeedsMaskNodeSelection => session.ViewModel.Masks.IsVectorMask && !session.ViewModel.Masks.HasSelectedPoint;
    public AnimationPropertyRowViewModel[] MaskRows { get => maskRows; private set => SetItems(ref maskRows, value, nameof(MaskRows)); }
    public NumericValueDraft? MaskPivotX => session.MaskEditing.Fields.FirstOrDefault(maskField => maskField.Target is null && maskField.Component == 0)?.Draft;
    public NumericValueDraft? MaskPivotY => session.MaskEditing.Fields.FirstOrDefault(maskField => maskField.Target is null && maskField.Component == 1)?.Draft;
    public ICommand OpenMaskPanelCommand { get; private set; } = null!;

    private void InitializeMaskPropertyGrid()
    {
        OpenMaskPanelCommand = new RelayCommand(() => session.ViewModel.ActivatePanel("masks"));
    }

    internal void RefreshMaskPropertyGrid()
    {
        var layer = session.SelectedLayer;
        MaskRows = layer is null ? [] : session.PropertyEditing.GetMaskRows(layer.Id, session.SceneEditing.MaskNodeId);
        foreach (var property in new[] { nameof(MaskExpanded), nameof(CanEditMask), nameof(HasMask),
            nameof(NeedsMaskNodeSelection), nameof(MaskPivotX), nameof(MaskPivotY) })
        {
            OnPropertyChanged(property);
        }
    }
}
