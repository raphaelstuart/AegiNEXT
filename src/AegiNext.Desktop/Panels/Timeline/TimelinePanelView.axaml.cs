using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using System.Windows.Input;
using Avalonia;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AegiNext.Desktop.Styling;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly TimelinePanelViewModel viewModel;
    private readonly SubtitleTimelineControl timeline;
    private readonly TimelineOverviewControl overview;
    private readonly MenuItem collapseTrackItem;
    private readonly MenuItem trackStyleItem;
    private readonly MenuItem autoTrackStyleItem;
    private readonly ToolbarToggleButton snapButton;
    private readonly ToolbarToggleButton stepButton;
    private readonly ToolbarToggleButton spectrumButton;
    private readonly ToolbarToggleButton waveformButton;
    private AegiNext.Media.Analysis.SpectrogramData? spectrum;
    private bool disposed;
    private bool applying;
    internal TimelinePanelView(TimelinePanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        timeline = this.FindControl<SubtitleTimelineControl>("Timeline")!;
        timeline.SetAudioGraphPalette(session.Preferences.AudioGraph);
        overview = this.FindControl<TimelineOverviewControl>("TimelineMinimap")!;
        snapButton = this.FindControl<ToolbarToggleButton>("TimelineSnapButton")!;
        stepButton = this.FindControl<ToolbarToggleButton>("TimelineStepButton")!;
        spectrumButton = this.FindControl<ToolbarToggleButton>("TimelineSpectrumButton")!;
        waveformButton = this.FindControl<ToolbarToggleButton>("TimelineWaveformButton")!;
        this.FindControl<PathIcon>("TimelineSnapIcon")!.Data = WorkbenchIcon.Create("Magnet").Data;
        this.FindControl<PathIcon>("TimelineSpectrumIcon")!.Data = WorkbenchIcon.Create("Spectrum").Data;
        this.FindControl<PathIcon>("TimelineWaveformIcon")!.Data = WorkbenchIcon.Create("Waveform").Data;
        TrackMenu = new();
        TrackMenu.Items.Add(CreateMenuItem("AddSubtitleTrackMenuItem", "AddTrack", viewModel.AddTrackCommand));
        TrackMenu.Items.Add(CreateMenuItem("RenameSubtitleTrackMenuItem", "RenameTrack", viewModel.RenameTrackCommand));
        TrackMenu.Items.Add(CreateMenuItem("DeleteSubtitleTrackMenuItem", "DeleteTrack", viewModel.DeleteTrackCommand));
        TrackMenu.Items.Add(new Separator());
        TrackMenu.Items.Add(CreateMenuItem("MoveSubtitleTrackUpMenuItem", "MoveTrackUp", viewModel.MoveTrackUpCommand));
        TrackMenu.Items.Add(CreateMenuItem("MoveSubtitleTrackDownMenuItem", "MoveTrackDown", viewModel.MoveTrackDownCommand));
        collapseTrackItem = new() { Name = "CollapseSubtitleTrackMenuItem" };
        collapseTrackItem.Click += (_, _) =>
        {
            if (viewModel.SelectedTrackId is { } id)
            {
                timeline.ToggleTrackCollapse(id);
            }
        };
        TrackMenu.Items.Add(collapseTrackItem);
        TrackMenu.Items.Add(new Separator());
        trackStyleItem = CreateMenuItem("SubtitleTrackStyleMenuItem", "TrackSubtitleStyle");
        autoTrackStyleItem = new()
        {
            Name = "AutoApplySubtitleTrackStyleMenuItem", ToggleType = MenuItemToggleType.CheckBox,
            Command = viewModel.ToggleTrackAutoStyleCommand
        };
        autoTrackStyleItem.Bind(MenuItem.HeaderProperty, Localization.Observe("Workbench.TrackStyleAutoApply").ToBinding());
        TrackMenu.Items.Add(trackStyleItem);
        TrackMenu.Items.Add(autoTrackStyleItem);
        timeline.TrackContextRequested += OnTrackContextRequested;
        timeline.SeekRequested += async (_, e) => await viewModel.SeekAsync(e.Time);
        timeline.ClipSelectionChanged += (_, e) => viewModel.SelectLayers(e);
        timeline.TrackSelected += (_, e) => viewModel.SelectTrack(e.Id);
        timeline.ViewportChanged += OnViewportChanged;
        overview.ViewportChanged += OnViewportChanged;
        timeline.TimingChanged += async (_, e) => await viewModel.CommitTimingAsync(e);
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = viewModel.SelectKeyframe(e);
        timeline.KeyframeMoved += async (_, e) => await viewModel.MoveKeyframeAsync(e);
        timeline.AddHandler(PointerPressedEvent, (_, _) => viewModel.IsSeeking = timeline.IsSeeking, RoutingStrategies.Bubble, true);
        timeline.AddHandler(PointerReleasedEvent, (_, _) => viewModel.IsSeeking = false, RoutingStrategies.Bubble, true);
        timeline.AddHandler(PointerCaptureLostEvent, (_, _) => viewModel.IsSeeking = false, RoutingStrategies.Bubble, true);
        timeline.SizeChanged += (_, _) =>
        {
            viewModel.RefreshViewport();
        };
        viewModel.PropertyChanged += OnViewModelChanged;
        session.PreferencesChanged += OnPreferencesChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        session.StyleLibraryChanged += OnStyleLibraryChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        ApplyState();
        RefreshTrackMenu();
        var nameInput = this.FindControl<TextBox>("TimelineTrackNameInput")!;
        nameInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                viewModel.CancelTrackRenameCommand.Execute(null);
                timeline.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && viewModel.ConfirmTrackRenameCommand.CanExecute(null))
            {
                viewModel.ConfirmTrackRenameCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    public string PanelId => "timeline";
    internal ContextMenu TrackMenu { get; }
    public void CancelGestures()
    {
        timeline.CancelGesture();
        overview.CancelGesture();
        viewModel.IsSeeking = false;
    }
    public void FocusInvalidField(string? fieldKey) => timeline.Focus();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyState();
        if (e.PropertyName is nameof(viewModel.Document) or nameof(viewModel.SelectedTrackId))
        {
            RefreshTrackMenu();
        }
        if (e.PropertyName == nameof(viewModel.IsRenamingTrack) && viewModel.IsRenamingTrack)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && viewModel.IsRenamingTrack)
                {
                    var input = this.FindControl<TextBox>("TimelineTrackNameInput")!;
                    input.Focus();
                    input.SelectAll();
                }
            });
        }
    }
    private void ApplyState()
    {
        if (applying || disposed)
        {
            return;
        }

        applying = true;
        try
        {
            timeline.Position = viewModel.Position;
            timeline.EffectProperty = viewModel.EffectProperty;
            timeline.IsSnapEnabled = viewModel.IsSnapEnabled;
            timeline.IsStepEnabled = viewModel.IsStepEnabled;
            timeline.IsSpectrumVisible = viewModel.IsSpectrumVisible;
            timeline.IsWaveformVisible = viewModel.IsWaveformVisible;
            timeline.SetDocument(viewModel.Document, viewModel.SelectedCueId, viewModel.SelectedLayer,
                viewModel.SelectedLayerIds.Count == 0 && viewModel.SelectedLayer is { } selected ? [selected.Id] : viewModel.SelectedLayerIds,
                viewModel.SelectedTrackId);
            timeline.SetViewport(viewModel.Viewport, viewModel.FullDuration);
            if (!ReferenceEquals(spectrum, viewModel.Spectrogram))
            {
                spectrum = viewModel.Spectrogram;
                timeline.SetSpectrogram(spectrum);
            }

            viewModel.Viewport = timeline.Viewport;
            overview.SetScene(viewModel.Document, viewModel.Viewport, viewModel.FullDuration, viewModel.Position);
        }
        finally
        {
            applying = false;
        }
    }
    private void OnViewportChanged(object? sender, TimelineViewportEventArgs e)
    {
        viewModel.Viewport = e.Viewport;
        if (!applying)
        {
            ApplyState();
        }
    }
    private void OnTrackContextRequested(object? sender, TimelineTrackContextEventArgs e)
    {
        TrackMenu.Close();
        if (e.TrackId is { } id && !viewModel.SelectTrack(id))
        {
            return;
        }

        RefreshTrackMenu();
        TrackMenu.Open(timeline);
    }
    private void RefreshTrackMenu()
    {
        collapseTrackItem.IsEnabled = viewModel.SelectedTrackId.HasValue;
        collapseTrackItem.Header = Localization.Get("Workbench." + (viewModel.SelectedTrackId is { } id && timeline.IsTrackCollapsed(id)
            ? "ExpandTrack" : "CollapseTrack"));
        RefreshStylePresets();
    }
    private void RefreshStylePresets()
    {
        trackStyleItem.Items.Clear();
        var presets = viewModel.StylePresets;
        trackStyleItem.IsEnabled = viewModel.SelectedTrackId.HasValue && presets.Length > 0;
        var currentTrack = viewModel.Document.SubtitleTracks.FirstOrDefault(track => track.Id == viewModel.SelectedTrackId);
        autoTrackStyleItem.IsEnabled = currentTrack is not null;
        autoTrackStyleItem.IsChecked = currentTrack?.AutoApplyStyle == true;
        autoTrackStyleItem.CommandParameter = currentTrack?.Id;
        foreach (var preset in presets)
        {
            if (currentTrack is null)
            {
                break;
            }
            trackStyleItem.Items.Add(new MenuItem
            {
                Header = preset.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = currentTrack.StylePresetId == preset.Id,
                Command = viewModel.ApplyTrackStyleCommand,
                CommandParameter = new TrackStylePresetRequest(currentTrack.Id, preset.Id)
            });
        }

        ToolTip.SetTip(trackStyleItem, presets.Length == 0 ? Localization.Get("Workbench.NoStylePresets") : currentTrack?.StylePresetName);
    }
    private void OnStyleLibraryChanged(object? sender, EventArgs e)
    {
        if (!disposed)
        {
            RefreshTrackMenu();
        }
    }
    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        timeline.SetAudioGraphPalette(session.Preferences.AudioGraph);
        timeline.InvalidateVisual();
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshTrackMenu();
    }
    private static MenuItem CreateMenuItem(string name, string key, ICommand? command = null)
    {
        var item = new MenuItem { Name = name, Command = command };
        item.Bind(MenuItem.HeaderProperty, Localization.Observe("Workbench." + key).ToBinding());
        return item;
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            viewModel.PropertyChanged -= OnViewModelChanged;
            session.PreferencesChanged -= OnPreferencesChanged;
            Localization.LanguageChanged -= OnLanguageChanged;
            session.StyleLibraryChanged -= OnStyleLibraryChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
            timeline.ViewportChanged -= OnViewportChanged;
            overview.ViewportChanged -= OnViewportChanged;
            timeline.TrackContextRequested -= OnTrackContextRequested;
            TrackMenu.Close();
            overview.CancelGesture();
            timeline.Dispose();
        }
    }
}
