using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly EffectsPanelViewModel viewModel;
    private readonly ComboBox blend;
    private readonly ComboBox property;
    private readonly ComboBox interpolation;
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    private bool disposed;
    internal EffectsPanelView(EffectsPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        blend = this.FindControl<ComboBox>("BlendCombo")!;
        blend.SelectionChanged += (_, _) =>
        {
            if (!session.IsUpdating)
            {
                viewModel.CommitBlend(blend.SelectedIndex);
            }
        };
        property = this.FindControl<ComboBox>("PropertyCombo")!;
        interpolation = this.FindControl<ComboBox>("InterpolationCombo")!;
        interpolation.SelectionChanged += (_, _) =>
        {
            if (!session.IsUpdating)
            {
                viewModel.CommitInterpolation(interpolation.SelectedIndex);
            }
        };
        viewModel.ChoicesRefreshing += OnChoicesRefreshing;
        viewModel.ChoicesRefreshed += OnChoicesRefreshed;
        var orientPath = this.FindControl<CheckBox>("OrientPathCheck")!;
        orientPath.IsCheckedChanged += (_, _) => viewModel.CommitOrientPath(orientPath.IsChecked == true);
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnCommitKeyDown, RoutingStrategies.Bubble);
        viewModel.KeyframeColorDraft.Committed += OnColorCommitted;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox or NumericUpDown)
            {
                var root = TopLevel.GetTopLevel(this);
                var suppressed = suppressFocusCommit;
                var revision = focusCommitRevision;
                var document = session.DocumentSnapshot;
                var layerId = session.SelectedLayerId;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!disposed && revision == focusCommitRevision && layerId == session.SelectedLayerId &&
                        ReferenceEquals(document, session.DocumentSnapshot) && !suppressed && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                        this.IsAttachedToVisualTree())
                    {
                        viewModel.CommitDrafts();
                    }
                }, DispatcherPriority.Background);
            }
        }, RoutingStrategies.Bubble);
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
    }
    public string PanelId => "effects";
    public void CancelGestures()
    {
        suppressFocusCommit = true;
        var revision = ++focusCommitRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == focusCommitRevision)
            {
                suppressFocusCommit = false;
            }
        }, DispatcherPriority.Background);
    }
    public void FocusInvalidField(string? fieldKey)
    {
        var control = fieldKey is { } key
            ? this.FindControl<Control>(key) ?? this.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Name == key)
            : null;
        if (control is ColorDraftInput color && color.TryFocusInvalidField())
        {
            return;
        }
        (control ?? this).Focus();
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        suppressFocusCommit = false;
        if (e.Key == Key.Escape && e.Source is Control source &&
            !source.GetSelfAndVisualAncestors().OfType<ColorDraftInput>().Any())
        {
            var maskInput = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
            if (viewModel.RestoreOperationField(maskInput?.Name))
            {
                e.Handled = true;
                return;
            }
            var field = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault() as Control ??
                source.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault(control => control.Name is not null && !control.Name.StartsWith("PART_", StringComparison.Ordinal));
            if (field?.Name is { } name)
            {
                viewModel.RestoreField(name);
                e.Handled = true;
            }
        }
    }

    private void OnColorCommitted(object? sender, ColorDraftCommittedEventArgs e) => viewModel.CommitDrafts();

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().Any())
        {
            viewModel.CommitDrafts();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EffectsPanelViewModel.ValidationError) or nameof(EffectsPanelViewModel.InvalidFieldKey))
        {
            foreach (var input in this.GetVisualDescendants().OfType<NumericDraftInput>())
            {
                DataValidationErrors.SetErrors(input, input.Name == viewModel.InvalidFieldKey && viewModel.ValidationError is { } message ? new[] { message } : null);
            }
        }
    }

    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();

    private void OnChoicesRefreshing(object? sender, EventArgs e)
    {
        BeginChoiceRefresh(blend);
        BeginChoiceRefresh(property);
        BeginChoiceRefresh(interpolation);
    }

    private void OnChoicesRefreshed(object? sender, EventArgs e)
    {
        blend.EndInit();
        property.EndInit();
        property.SelectedItem = viewModel.SelectedProperty;
        interpolation.EndInit();
    }

    private static void BeginChoiceRefresh(ComboBox choice)
    {
        var selectedIndex = choice.SelectedIndex;
        choice.BeginInit();
        choice.SelectedIndex = selectedIndex;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            viewModel.ChoicesRefreshing -= OnChoicesRefreshing;
            viewModel.ChoicesRefreshed -= OnChoicesRefreshed;
            viewModel.KeyframeColorDraft.Committed -= OnColorCommitted;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        }
    }
}
