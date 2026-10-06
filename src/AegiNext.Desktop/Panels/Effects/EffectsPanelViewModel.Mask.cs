using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    public NumericValueDraft PowerExponent { get; } = new() { PreserveDoublePrecision = true };
    public bool IsPowerInterpolation => Interpolation == (int)KeyframeInterpolation.POWER;
    private bool isOrderedTransform;
    public bool IsOrderedTransform
    {
        get => isOrderedTransform;
        internal set
        {
            if (SetProperty(ref isOrderedTransform, value))
            {
                OnPropertyChanged(nameof(CanEditKeyframes));
            }
        }
    }
    public bool CanEditKeyframes => !IsOrderedTransform;
    internal double ReadPowerExponent()
    {
        if (!double.TryParse(PowerExponent.RawText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) ||
            !double.IsFinite(value) || value <= 0)
        {
            session.ViewModel.InvalidPanelId = "effects";
            session.ViewModel.InvalidFieldKey = "PowerExponentInput";
            throw new InvalidDataException(Localization.Get("Workbench.InvalidValue"));
        }
        return value;
    }

    internal void LoadPowerExponent(double value) => LoadFiniteDraft(PowerExponent, value);

    private static void LoadFiniteDraft(NumericValueDraft draft, double value)
    {
        if (value > (double)decimal.MinValue && value < (double)decimal.MaxValue && (value == 0 || (decimal)value != 0))
        {
            draft.Load(value);
        }
        else
        {
            draft.RawText = value.ToString("G17", System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;
    internal void RefreshMaskState() => RefreshChoices(Blends, session.MaskPropertyChoices(), Interpolations);

    public AnimationTransformChoice[] TransformOperations { get; private set; } = [];
    private AnimationTransformChoice? selectedOperation;
    public AnimationTransformChoice? SelectedOperation
    {
        get => selectedOperation;
        set
        {
            if (!loadingOperation && !session.IsUpdating && !session.TryCommitDrafts())
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref selectedOperation, value))
            {
                session.SceneEditing.TransformOperationId = value?.Id;
                LoadOperation();
            }
        }
    }
    public NumericValueDraft OperationStart { get; } = new();
    public NumericValueDraft OperationEnd { get; } = new();
    public NumericValueDraft OperationValueX { get; } = new();
    public NumericValueDraft OperationValueY { get; } = new();
    public NumericValueDraft OperationAcceleration { get; } = new() { PreserveDoublePrecision = true };
    public NumericValueDraft OperationOrder { get; } = new();
    public ICommand DeleteOperationCommand => new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(() =>
    {
        if (SelectedLayer is { } layer && SelectedOperation is { } operation)
        {
            session.Editor.RemoveAnimationTransform(layer.Id, Target, operation.Id);
        }
    })));

    internal void RefreshTransformOperations(AnimationTrack? track)
    {
        TransformOperations = track?.Transforms.Select((operation, index) => new AnimationTransformChoice(operation.Id,
            $"{index + 1} · {Seconds(operation.Start):0.###}–{Seconds(operation.End):0.###}" )).ToArray() ?? [];
        selectedOperation = TransformOperations.FirstOrDefault(operation => operation.Id == session.SceneEditing.TransformOperationId)
            ?? TransformOperations.FirstOrDefault();
        session.SceneEditing.TransformOperationId = selectedOperation?.Id;
        OnPropertyChanged(nameof(TransformOperations));
        OnPropertyChanged(nameof(SelectedOperation));
        LoadOperation();
    }

    private void LoadOperation()
    {
        if (HasOperationDraft && operationLayer == SelectedLayer?.Id)
        {
            return;
        }
        var track = SelectedLayer?.Tracks.FirstOrDefault(track => track.Target == Target);
        var operation = track?.Transforms.FirstOrDefault(operation => operation.Id == SelectedOperation?.Id);
        if (operation is null)
        {
            return;
        }
        loadingOperation = true;
        try
        {
            OperationStart.Load(Seconds(operation.Start));
            OperationEnd.Load(Seconds(operation.End));
            OperationValueX.Load(operation.Value.GetComponent(0));
            OperationValueY.Load(operation.Value.IsVector ? operation.Value.Vector.Y : 0);
            LoadFiniteDraft(OperationAcceleration, operation.Acceleration);
            OperationOrder.Load(track!.Transforms.IndexOf(operation) + 1);
            operationOriginals.Clear();
            foreach (var field in OperationFields)
            {
                operationOriginals[field.Name] = field.Draft.RawText;
            }
        }
        finally
        {
            loadingOperation = false;
        }
    }

}
