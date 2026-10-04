using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controllers;
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
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    private bool disposed;
    internal EffectsPanelView(EffectsPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        var blend = this.FindControl<ComboBox>("BlendCombo")!;
        blend.SelectionChanged += (_, _) => viewModel.CommitBlend(blend.SelectedIndex);
        var interpolation = this.FindControl<ComboBox>("InterpolationCombo")!;
        interpolation.SelectionChanged += (_, _) => viewModel.CommitInterpolation(interpolation.SelectedIndex);
        var invertMask = this.FindControl<CheckBox>("InvertMaskCheck")!;
        invertMask.IsCheckedChanged += (_, _) => viewModel.CommitInvertMask(invertMask.IsChecked == true);
        var orientPath = this.FindControl<CheckBox>("OrientPathCheck")!;
        orientPath.IsCheckedChanged += (_, _) => viewModel.CommitOrientPath(orientPath.IsChecked == true);
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
    }
    public void FocusInvalidField(string? fieldKey) => (fieldKey is { } key ? this.FindControl<Control>(key) ?? this : this).Focus();
    private void OnPreferencesChanged(object? sender, EventArgs e) => ControlLocalization.Apply(this);
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            session.PreferencesChanged -= OnPreferencesChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        }
    }
}
