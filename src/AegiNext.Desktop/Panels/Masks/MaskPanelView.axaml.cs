using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Masks;

internal sealed partial class MaskPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly MaskPanelViewModel viewModel;
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    private bool disposed;

    internal MaskPanelView(MaskPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnCommitKeyDown, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        BindNumericDrags();
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        viewModel.PropertyChanged += OnViewModelChanged;
    }

    public string PanelId => "masks";

    /// <inheritdoc />
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.END_TEXT_INPUT && (viewModel.IsRectangleTool || viewModel.IsVectorTool) &&
            focusedElement is Control control && !control.GetSelfAndVisualAncestors().Any(ancestor => ancestor is TextBox or NumericUpDown);
    }

    /// <inheritdoc />
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }
        session.MaskEditing.ExitEditing();
        return true;
    }

    /// <inheritdoc />
    public void CancelGestures()
    {
        CancelNumericDrags();
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

    /// <inheritdoc />
    public void FocusInvalidField(string? fieldKey)
    {
        if (fieldKey is null)
        {
            Focus();
            return;
        }
        foreach (var vector in this.GetVisualDescendants().OfType<VectorDraftInput>())
        {
            if (vector.FocusField(fieldKey))
            {
                return;
            }
        }
        var scalar = this.GetVisualDescendants().OfType<NumericDraftInput>()
            .FirstOrDefault(input => input.DataContext is MaskNumericField field && field.Key == fieldKey);
        if (scalar is not null)
        {
            scalar.BringIntoView();
            scalar.Focus();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGestures();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        suppressFocusCommit = false;
        if (e.Key == Key.Escape && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault() is { } input)
        {
            viewModel.RestoreField(input.DataContext is MaskNumericField field ? field.Key : input.Name);
            e.Handled = true;
        }
    }

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().Any())
        {
            viewModel.CommitDrafts();
            e.Handled = true;
        }
    }

    private void OnLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control source && source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>()
            .Any(input => input.IsTitleDragging))
        {
            return;
        }
        if (e.Source is not (TextBox or NumericUpDown))
        {
            return;
        }
        var root = TopLevel.GetTopLevel(this);
        var suppressed = suppressFocusCommit;
        var revision = focusCommitRevision;
        var document = session.DocumentSnapshot;
        var layerId = session.SelectedLayerId;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && !suppressed && revision == focusCommitRevision && layerId == session.SelectedLayerId &&
                ReferenceEquals(document, session.DocumentSnapshot) && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                this.IsAttachedToVisualTree())
            {
                viewModel.CommitDrafts();
            }
        }, DispatcherPriority.Background);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaskPanelViewModel.ValidationError) or nameof(MaskPanelViewModel.InvalidFieldKey))
        {
            foreach (var input in this.GetVisualDescendants().OfType<NumericDraftInput>())
            {
                var key = input.DataContext is MaskNumericField field ? field.Key : input.Name;
                DataValidationErrors.SetErrors(input, key == viewModel.InvalidFieldKey && viewModel.ValidationError is { } error ? new[] { error } : null);
            }
        }
    }

    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        focusCommitRevision++;
        session.NumericGestureCancellationRequested -= OnNumericGestureCancellation;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        viewModel.PropertyChanged -= OnViewModelChanged;
    }
}
