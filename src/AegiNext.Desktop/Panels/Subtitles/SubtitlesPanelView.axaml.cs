using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Styling;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed partial class SubtitlesPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly ListBox list;
    private readonly SubtitlesPanelViewModel viewModel;
    private readonly HashSet<TextBox> caretInputs = [];
    private bool suppressFocusCommit;
    private int focusCommitRevision;
    internal SubtitlesPanelView(SubtitlesPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        var tracks = this.FindControl<ComboBox>("SubtitleTrackCombo")!;
        tracks.SelectionChanged += (_, _) =>
        {
            if (tracks.SelectedItem is AegiNext.Core.Projects.SubtitleTrack track)
            {
                viewModel.SelectTrack(track.Id);
            }
        };
        list = this.FindControl<ListBox>("SubtitleList")!;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is SubtitleRow row)
            {
                viewModel.SelectCue(row.Id);
            }
        };
        AddHandler(PointerPressedEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, _) => suppressFocusCommit = false, RoutingStrategies.Tunnel);
        list.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox { AcceptsReturn: true, DataContext: SubtitleRow row } box)
            {
                viewModel.SetCaret(row.Id, box.CaretIndex);
                if (caretInputs.Add(box))
                {
                    box.PropertyChanged += OnTextBoxPropertyChanged;
                    box.DetachedFromVisualTree += OnCaretInputDetached;
                }
            }
        }, RoutingStrategies.Bubble);
        list.AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox { DataContext: SubtitleRow row } box)
            {
                if (box.AcceptsReturn)
                {
                    viewModel.SetCaret(row.Id, box.CaretIndex);
                }
                var root = TopLevel.GetTopLevel(this);
                var suppressed = suppressFocusCommit;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!suppressed && root is not null && ReferenceEquals(root, TopLevel.GetTopLevel(this)) &&
                        this.IsAttachedToVisualTree())
                    {
                        viewModel.CommitRow(row);
                    }
                }, DispatcherPriority.Background);
            }
        }, RoutingStrategies.Bubble);
        session.PreferencesChanged += OnPreferencesChanged;
        session.SubtitleScrollRequested += OnScrollRequested;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        RefreshLocalization();
    }

    public string PanelId => "subtitles";
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
        var row = session.ViewModel.Subtitles.Rows.FirstOrDefault(value => value.Id == session.ViewModel.Subtitles.InvalidRowId);
        if (row is null)
        {
            list.Focus();
            return;
        }
        list.ScrollIntoView(row);
        Dispatcher.UIThread.Post(() =>
        {
            var column = fieldKey switch { "StartText" => 1, "EndText" => 2, _ => 4 };
            this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => ReferenceEquals(box.DataContext, row) &&
                Grid.GetColumn(box) == column)?.Focus();
        }, DispatcherPriority.Loaded);
    }
    private void OnTextBoxPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty && sender is TextBox { DataContext: SubtitleRow row } box)
        {
            viewModel.SetCaret(row.Id, box.CaretIndex);
        }
    }
    private void OnCaretInputDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            box.PropertyChanged -= OnTextBoxPropertyChanged;
            box.DetachedFromVisualTree -= OnCaretInputDetached;
            caretInputs.Remove(box);
        }
    }
    private void OnPreferencesChanged(object? sender, EventArgs e) => RefreshLocalization();
    private void RefreshLocalization()
    {
        ControlLocalization.Apply(this);
        this.FindControl<Button>("MoveSubtitleTrackUpButton")!.Content =
            WorkbenchIcon.Content(WorkbenchText.Get("MoveTrackUp"), "Up");
        this.FindControl<Button>("MoveSubtitleTrackDownButton")!.Content =
            WorkbenchIcon.Content(WorkbenchText.Get("MoveTrackDown"), "Down");
    }
    private void OnScrollRequested(object? sender, EventArgs e)
    {
        if (list.SelectedItem is { } selected)
        {
            list.ScrollIntoView(selected);
        }
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        session.PreferencesChanged -= OnPreferencesChanged;
        session.SubtitleScrollRequested -= OnScrollRequested;
        session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
        foreach (var box in caretInputs)
        {
            box.PropertyChanged -= OnTextBoxPropertyChanged;
            box.DetachedFromVisualTree -= OnCaretInputDetached;
        }
        caretInputs.Clear();
    }
}
