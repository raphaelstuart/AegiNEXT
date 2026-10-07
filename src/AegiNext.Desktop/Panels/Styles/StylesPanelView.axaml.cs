using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class StylesPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly StylesPanelViewModel viewModel;
    private readonly FontFamilyPicker fonts;
    private readonly SubtitleAlignmentPicker alignment;
    private bool suppressFocusCommit;
    private bool formattingPointerActive;
    private bool formattingFocusPending;
    private int focusCommitRevision;
    private bool disposed;
    internal StylesPanelView(StylesPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        fonts = this.FindControl<FontFamilyPicker>("FontCombo")!;
        fonts.CommitOnLostFocus = false;
        fonts.RefreshFontCandidates(viewModel.Fonts.Candidates);
        fonts.SetCurrentFont(viewModel.CurrentFont);
        fonts.FamilyCommitted += (_, e) =>
        {
            viewModel.CommitFont(e.Selection);
        };
        var bold = this.FindControl<CheckBox>("BoldCheck")!;
        bold.IsCheckedChanged += (_, _) =>
        {
            if (!session.IsUpdating)
            {
                viewModel.CommitBold(bold.IsChecked == true);
            }
        };
        var italic = this.FindControl<CheckBox>("ItalicCheck")!;
        italic.IsCheckedChanged += (_, _) =>
        {
            if (!session.IsUpdating)
            {
                viewModel.CommitItalic(italic.IsChecked == true);
            }
        };
        viewModel.FillDraft.Committed += (_, _) => viewModel.CommitDrafts();
        viewModel.StrokeDraft.Committed += (_, _) => viewModel.CommitDrafts();
        viewModel.ShadowDraft.Committed += OnShadowCommitted;
        alignment = this.FindControl<SubtitleAlignmentPicker>("AlignmentPicker")!;
        alignment.AlignmentCommitted += (_, e) =>
        {
            if (!session.IsUpdating)
            {
                viewModel.CommitAlignment((int)e.Alignment);
            }
        };
        var position = this.FindControl<SubtitlePositionEditor>("PositionEditor")!;
        position.AutomaticPositionRequested += async (_, _) => await viewModel.RestoreAutomaticPositionAsync();
        position.ExplicitPositionChanged += (_, _) => viewModel.CommitDrafts();
        position.PresetPositionChanged += (_, _) => viewModel.CommitDrafts();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.CurrentFont))
            {
                fonts.SetCurrentFont(viewModel.CurrentFont);
            }
        };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            suppressFocusCommit = false;
            if (e.Source is Control source && source.GetSelfAndVisualAncestors().Any(value =>
                ReferenceEquals(value, bold) || ReferenceEquals(value, italic) || ReferenceEquals(value, alignment)))
            {
                formattingPointerActive = true;
                formattingFocusPending = false;
                ++focusCommitRevision;
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => EndFormattingPointer(), RoutingStrategies.Tunnel, true);
        bold.PointerCaptureLost += (_, _) => EndFormattingPointer();
        italic.PointerCaptureLost += (_, _) => EndFormattingPointer();
        alignment.AddHandler(PointerCaptureLostEvent, (_, _) => EndFormattingPointer(), RoutingStrategies.Bubble, true);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source is Control source &&
                source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().Any())
            {
                viewModel.CommitDrafts();
                e.Handled = true;
            }
        }, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox or NumericUpDown or FontFamilyPicker)
            {
                if (formattingPointerActive)
                {
                    formattingFocusPending = true;
                    return;
                }
                QueueFocusCommit();
            }
        }, RoutingStrategies.Bubble);
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
    }

    public string PanelId => "styles";
    private void EndFormattingPointer()
    {
        if (!formattingPointerActive)
        {
            return;
        }
        formattingPointerActive = false;
        if (formattingFocusPending)
        {
            formattingFocusPending = false;
            QueueFocusCommit();
        }
    }
    private void QueueFocusCommit()
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
                if (formattingPointerActive)
                {
                    formattingFocusPending = true;
                    return;
                }
                viewModel.CommitDrafts();
            }
        }, DispatcherPriority.Background);
    }
    public void CancelGestures()
    {
        formattingPointerActive = formattingFocusPending = false;
        suppressFocusCommit = true;
        var revision = ++focusCommitRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == focusCommitRevision)
            {
                suppressFocusCommit = false;
            }
        }, DispatcherPriority.Background);
        fonts.IsDropDownOpen = false;
    }
    public void FocusInvalidField(string? fieldKey)
    {
        if (fieldKey is not null && this.FindControl<VectorDraftInput>("ShadowOffsetInput")!.FocusField(fieldKey))
        {
            return;
        }
        if (fieldKey is not null && this.FindControl<SubtitlePositionEditor>("PositionEditor")!.FocusInvalidField(fieldKey))
        {
            return;
        }

        var control = fieldKey is null ? fonts : this.FindControl<Control>(fieldKey) ?? fonts;
        if (control is ColorDraftInput color && color.TryFocusInvalidField())
        {
            return;
        }
        if (control is NumericDraftInput numeric && numeric.FocusInput())
        {
            return;
        }
        control.Focus();
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        suppressFocusCommit = false;
        if (e.Key == Key.Escape && e.Source is Control source &&
            !source.GetSelfAndVisualAncestors().OfType<ColorDraftInput>().Any())
        {
            var field = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
            if (field?.Name is { } name && viewModel.RestoreNumberField(name))
            {
                focusCommitRevision++;
                e.Handled = true;
            }
        }
    }

    private void OnShadowCommitted(object? sender, ColorDraftCommittedEventArgs e) => viewModel.CommitDrafts();

    public void Dispose()
    {
        disposed = true;
        focusCommitRevision++;
        viewModel.ShadowDraft.Committed -= OnShadowCommitted;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
    }
}
