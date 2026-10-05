using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
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
    private readonly FontFamilyPicker fonts;
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    internal StylesPanelView(StylesPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        fonts = this.FindControl<FontFamilyPicker>("FontCombo")!;
        fonts.CommitOnLostFocus = false;
        fonts.SetCurrentFamily(viewModel.FontFamily);
        fonts.FamilyCommitted += (_, e) =>
        {
            viewModel.CommitFont(e.FamilyName);
        };
        var bold = this.FindControl<CheckBox>("BoldCheck")!;
        bold.IsCheckedChanged += (_, _) => viewModel.CommitBold(bold.IsChecked == true);
        var italic = this.FindControl<CheckBox>("ItalicCheck")!;
        italic.IsCheckedChanged += (_, _) => viewModel.CommitItalic(italic.IsChecked == true);
        viewModel.FillDraft.Committed += (_, _) => viewModel.CommitDrafts();
        viewModel.StrokeDraft.Committed += (_, _) => viewModel.CommitDrafts();
        var alignment = this.FindControl<ComboBox>("AlignmentCombo")!;
        alignment.SelectionChanged += (_, _) => viewModel.CommitAlignment(alignment.SelectedIndex);
        var position = this.FindControl<SubtitlePositionEditor>("PositionEditor")!;
        position.AutomaticPositionRequested += async (_, _) => await viewModel.RestoreAutomaticPositionAsync();
        position.ExplicitPositionChanged += (_, _) => viewModel.CommitDrafts();
        position.PresetPositionChanged += (_, _) => viewModel.CommitDrafts();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.FontFamily))
            {
                fonts.SetCurrentFamily(viewModel.FontFamily);
            }
        };
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox or NumericUpDown or FontFamilyPicker)
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

    public string PanelId => "styles";
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
        fonts.IsDropDownOpen = false;
    }
    public void FocusInvalidField(string? fieldKey)
    {
        if (fieldKey is not null && this.FindControl<SubtitlePositionEditor>("PositionEditor")!.FocusInvalidField(fieldKey))
        {
            return;
        }

        var control = fieldKey is null ? fonts : this.FindControl<Control>(fieldKey) ?? fonts;
        if (control is ColorDraftInput color && color.TryFocusInvalidField())
        {
            return;
        }
        control.Focus();
    }
    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        ControlLocalization.Apply(this);
        this.FindControl<ColorDraftInput>("FillPicker")!.RefreshLanguage();
        this.FindControl<ColorDraftInput>("StrokePicker")!.RefreshLanguage();
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        session.PreferencesChanged -= OnPreferencesChanged;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
    }
}
