using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly EffectsPanelViewModel viewModel;
    private readonly EffectCanvasControl canvas;
    private readonly ListBox layers;
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    internal EffectsPanelView(EffectsPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        canvas = this.FindControl<EffectCanvasControl>("EffectCanvas")!;
        layers = this.FindControl<ListBox>("LayerList")!;
        layers.SelectionChanged += (_, _) =>
        {
            if (layers.SelectedItem is LayerListItem selected)
            {
                viewModel.SelectLayer(selected.Id, layers.SelectedItems?.OfType<LayerListItem>().Select(value => value.Id).ToArray() ?? []);
            }
        };
        canvas.LayerEdited += async (_, e) => await viewModel.CommitCanvasAsync(e);
        var blend = this.FindControl<ComboBox>("BlendCombo")!;
        blend.SelectionChanged += (_, _) => viewModel.CommitBlend(blend.SelectedIndex);
        var interpolation = this.FindControl<ComboBox>("InterpolationCombo")!;
        interpolation.SelectionChanged += (_, _) => viewModel.CommitInterpolation(interpolation.SelectedIndex);
        var invertMask = this.FindControl<CheckBox>("InvertMaskCheck")!;
        invertMask.IsCheckedChanged += (_, _) => viewModel.CommitInvertMask(invertMask.IsChecked == true);
        var orientPath = this.FindControl<CheckBox>("OrientPathCheck")!;
        orientPath.IsCheckedChanged += (_, _) => viewModel.CommitOrientPath(orientPath.IsChecked == true);
        viewModel.PropertyChanged += OnViewModelChanged;
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox or NumericUpDown)
            {
                var root = TopLevel.GetTopLevel(this);
                var suppressed = suppressFocusCommit;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!suppressed && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                        this.IsAttachedToVisualTree())
                    {
                        viewModel.CommitDrafts();
                    }
                }, DispatcherPriority.Background);
            }
        }, RoutingStrategies.Bubble);
        session.PreferencesChanged += OnPreferencesChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        ApplyScene();
        ControlLocalization.Apply(this);
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
        canvas.CancelGesture();
    }
    public void FocusInvalidField(string? fieldKey) => (fieldKey is { } key ? this.FindControl<Control>(key) ?? layers : layers).Focus();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Document" or "SelectedLayer" or "Position" or "EditMode")
        {
            ApplyScene();
        }
    }
    private void ApplyScene()
    {
        canvas.EditMode = viewModel.EditMode;
        canvas.SetScene(viewModel.Document, viewModel.SelectedLayer, viewModel.Position);
    }
    private void OnPreferencesChanged(object? sender, EventArgs e) => ControlLocalization.Apply(this);
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;
        session.PreferencesChanged -= OnPreferencesChanged;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
    }
}
