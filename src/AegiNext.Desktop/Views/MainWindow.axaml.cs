using System.ComponentModel;
using System.Windows.Input;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Panels;
using AegiNext.Desktop.Panels.Preview;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Panels.Subtitles;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Views;

/// <summary>组合固定功能面板、窗口宿主与工作台会话的桌面主窗口。</summary>
public sealed partial class MainWindow : Window, IAsyncDisposable
{
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly WorkbenchMenuCatalog menuCatalog;
    private readonly WorkbenchWindowRegistry windowRegistry;
    private readonly Dictionary<string, Control> panels;
    private readonly ContentControl workspaceHost;
    private readonly WorkbenchLayoutController layouts;
    private readonly Dictionary<string, ICommand> presetCommands = new(StringComparer.Ordinal);
    private readonly Dictionary<Window, string> floatingTitles = [];
    private bool closing;
    private bool closeCompleted;
    private Task? disposeTask;
    private SettingsWindow? settingsWindow;

    /// <summary>通过显式组合根创建唯一工作台会话和固定长生命周期面板。</summary>
    public MainWindow() : this(null)
    {
    }

    internal MainWindow(Func<Action<VideoPreviewUpdate>, VideoPreviewController>? controllerFactory)
    {
        AvaloniaXamlLoader.Load(this);
        Session = WorkbenchCompositionRoot.Create(new WindowWorkbenchDialogService(this,
            registerWindow: RegisterAuxiliaryWindow), controllerFactory);
        ViewModel = Session.ViewModel;
        DataContext = ViewModel;
        panels = new(StringComparer.Ordinal)
        {
            ["preview"] = new PreviewPanelView(ViewModel.Preview, Session),
            ["timeline"] = new TimelinePanelView(ViewModel.Timeline, Session),
            ["subtitles"] = new SubtitlesPanelView(ViewModel.Subtitles, Session),
            ["styles"] = new StylesPanelView(ViewModel.Styles, Session),
            ["effects"] = new EffectsPanelView(ViewModel.Effects, Session),
            ["export"] = new ExportPanelView(ViewModel.Export, Session),
            [WorkbenchPanelIds.LOG] = new LogPanelView(ViewModel.Log, Session.Journal, () => Session.IsClosing)
        };
        workspaceHost = this.FindControl<ContentControl>("WorkspaceHost")!;
        menuCatalog = new(ViewModel.GetCommand);
        windowRegistry = new(menuCatalog, Session.InvalidateTimingSession, ViewModel.CancelGestures);
        windowRegistry.Register(this, () => ViewModel.Title, this.FindControl<WindowTitleBar>("TitleBar")!,
            WorkbenchWindowRole.MAIN);
        layouts = new(this, panels, Session.PreferencesStore.DirectoryPath, ViewModel.TryCommitDrafts,
            ViewModel.CancelGestures, RegisterWorkspaceWindow);
        workspaceHost.Content = layouts.Host;
        layouts.Changed += OnLayoutChanged;
        layouts.Error += OnLayoutError;
        layouts.FloatingWindowTitleChanged += OnFloatingWindowTitleChanged;
        ViewModel.HostCommandHandler = HandleHostCommandAsync;
        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.Log.PropertyChanged += OnLogChanged;
        ViewModel.DraftErrorFocusRequested += OnDraftErrorFocusRequested;
        Session.PreferencesChanged += OnPreferencesChanged;
        Session.StyleLibraryChanged += OnStyleLibraryChanged;
        Session.SelectionChanged += OnSelectionChanged;
        clockTimer.Tick += (_, _) => Session.Tick();
        Opened += (_, _) => clockTimer.Start();
        Closed += (_, _) => clockTimer.Stop();
        ApplyWindowPreferences();
        RefreshPanelAvailability();
        RefreshLogIndicator();
        if (layouts.LastError is { } layoutError)
        {
            Session.ShowError(new InvalidOperationException(layoutError));
        }
    }

    internal WorkbenchSession Session { get; }
    internal WorkbenchViewModel ViewModel { get; }
    internal IReadOnlyDictionary<string, Control> Panels => panels;
    internal WorkbenchLayoutController Layouts => layouts;
    internal WorkbenchWindowRegistry WindowRegistry => windowRegistry;
    internal ProjectDocument DocumentSnapshot => Session.DocumentSnapshot;
    internal ICommand GetCommand(WorkbenchCommand command) => ViewModel.GetCommand(command);
    internal Task OpenMediaAsync(string path, bool updateProject) => Session.OpenMediaAsync(path, updateProject);

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!closeCompleted && disposeTask?.IsCompletedSuccessfully != true)
        {
            e.Cancel = true;
            if (!closing)
            {
                closing = true;
                _ = CompleteCloseAsync();
            }
        }
        base.OnClosing(e);
    }

    private async Task CompleteCloseAsync()
    {
        try
        {
            if (!await Session.RequestCloseAsync(layouts.FlushAsync))
            {
                closing = false;
                return;
            }
            await DisposeAsync();
            closeCompleted = true;
            Close();
        }
        catch (Exception error)
        {
            Session.ShowError(error);
            closing = false;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        disposeTask ??= DisposeCoreAsync();
        return new(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        ViewModel.Log.PropertyChanged -= OnLogChanged;
        workspaceHost.IsEnabled = false;
        clockTimer.Stop();
        settingsWindow?.Close();
        try
        {
            try
            {
                await layouts.FlushAsync();
            }
            finally
            {
                layouts.Dispose();
                await Session.DisposeAsync();
            }
        }
        finally
        {
            foreach (var panel in panels.Values.OfType<IWorkbenchPanelView>())
            {
                panel.Dispose();
            }
            ViewModel.PropertyChanged -= OnViewModelChanged;
            ViewModel.DraftErrorFocusRequested -= OnDraftErrorFocusRequested;
            Session.PreferencesChanged -= OnPreferencesChanged;
            Session.StyleLibraryChanged -= OnStyleLibraryChanged;
            Session.SelectionChanged -= OnSelectionChanged;
            layouts.Changed -= OnLayoutChanged;
            layouts.Error -= OnLayoutError;
            layouts.FloatingWindowTitleChanged -= OnFloatingWindowTitleChanged;
            windowRegistry.Dispose();
        }
    }

    private void RegisterAuxiliaryWindow(Window window)
    {
        windowRegistry.RegisterAuxiliary(window);
    }

    private void RegisterWorkspaceWindow(Window window)
    {
        windowRegistry.Register(window, () => window switch
        {
            WorkbenchFloatingHostWindow => $"{ViewModel.Title} — {floatingTitles.GetValueOrDefault(window, string.Empty)}",
            LayoutPresetManagerWindow => $"{LayoutText.Get("Manage", Session.InterfaceCulture)} — AegiNext",
            _ => $"{LayoutText.Get("SaveAs", Session.InterfaceCulture)} — AegiNext"
        }, role: window is WorkbenchFloatingHostWindow ? WorkbenchWindowRole.FLOATING : WorkbenchWindowRole.AUXILIARY);
        window.Closed += (_, _) => floatingTitles.Remove(window);
    }

    private async Task HandleHostCommandAsync(WorkbenchHostCommandEventArgs request)
    {
        if (request.Command == WorkbenchCommand.OPEN_SETTINGS)
        {
            OpenSettings(request.SettingsPage);
        }
        else if (request.Command == WorkbenchCommand.EXIT)
        {
            Close();
        }
        else if (request.Command == WorkbenchCommand.LAYOUT_SAVE)
        {
            if (layouts.Presets.Single(preset => preset.Id == layouts.CurrentPresetId).IsReadOnly)
            {
                await layouts.ShowSaveAsAsync();
            }
            else
            {
                await layouts.SaveAsync();
            }
        }
        else if (request.Command == WorkbenchCommand.LAYOUT_SAVE_AS)
        {
            await layouts.ShowSaveAsAsync();
        }
        else if (request.Command == WorkbenchCommand.LAYOUT_MANAGE)
        {
            await layouts.ShowManagerAsync();
        }
        else if (request.Command == WorkbenchCommand.LAYOUT_RESTORE_DEFAULT)
        {
            await layouts.RestoreDefaultAsync();
        }
        else
        {
            var id = request.Command switch
            {
                WorkbenchCommand.VIEW_PREVIEW => "preview", WorkbenchCommand.VIEW_TIMELINE => "timeline",
                WorkbenchCommand.VIEW_SUBTITLES => "subtitles", WorkbenchCommand.VIEW_STYLES => "styles",
                WorkbenchCommand.VIEW_EFFECTS => "effects", WorkbenchCommand.VIEW_EXPORT => "export",
                WorkbenchCommand.VIEW_LOG => WorkbenchPanelIds.LOG, _ => null
            };
            if (id is not null)
            {
                layouts.Activate(id);
            }
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => RefreshLayoutMenu();
    private void OnLogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPanelViewModel.UnreadErrorCount))
        {
            RefreshLogIndicator();
        }
    }

    private void RefreshLogIndicator()
    {
        var count = ViewModel.Log.UnreadErrorCount;
        layouts.SetPanelUnreadCount(WorkbenchPanelIds.LOG, count);
        menuCatalog.UpdateUnreadLogErrors(count);
    }
    private void OnLayoutError(object? sender, EventArgs e)
    {
        if (layouts.LastError is { } error)
        {
            Session.ShowError(new InvalidOperationException(error));
        }
    }
    private void OnFloatingWindowTitleChanged(Window window, string title)
    {
        floatingTitles[window] = title;
        windowRegistry.RefreshTitles();
    }
    private void RefreshLayoutMenu()
    {
        var choices = layouts.Presets.Select(preset =>
        {
            if (!presetCommands.TryGetValue(preset.Id, out var command))
            {
                command = new AsyncRelayCommand(async () =>
                {
                    await Session.RunCommandAsync(() => layouts.ApplyPresetAsync(preset.Id));
                    RefreshLayoutMenu();
                }, () => !Session.IsProjectBusy && !Session.IsClosing);
                presetCommands.Add(preset.Id, command);
            }
            return new LayoutMenuChoice(preset.Id, layouts.GetPresetName(preset),
                preset.Id == layouts.CurrentPresetId, command);
        }).ToArray();
        menuCatalog.UpdateLayouts(choices, layouts.IsModified);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Title))
        {
            windowRegistry.RefreshTitles();
        }
        else if (e.PropertyName == nameof(ViewModel.Error))
        {
            settingsWindow?.ShowError(ViewModel.Error);
        }
        else if (e.PropertyName == nameof(ViewModel.IsBusy))
        {
            RefreshPanelAvailability();
            foreach (var command in presetCommands.Values.OfType<AsyncRelayCommand>())
            {
                command.NotifyCanExecuteChanged();
            }
        }
    }
    private void OnDraftErrorFocusRequested(object? sender, EventArgs e)
    {
        var id = ViewModel.InvalidPanelId ?? "subtitles";
        if (panels.TryGetValue(id, out var panel) && panel is IWorkbenchPanelView input)
        {
            layouts.Activate(id);
            input.FocusInvalidField(ViewModel.InvalidFieldKey);
        }
    }
    private void RefreshPanelAvailability()
    {
        foreach (var pair in panels)
        {
            if (pair.Key != WorkbenchPanelIds.LOG)
            {
                pair.Value.SetCurrentValue(InputElement.IsEnabledProperty, !ViewModel.IsBusy);
            }
        }
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) => ApplyWindowPreferences();
    private void OnStyleLibraryChanged(object? sender, EventArgs e)
    {
        settingsWindow?.UpdateStyles(Session.StyleLibrary.Snapshot.Presets);
        settingsWindow?.SetStyleOperationBusy(Session.Styles.IsBusy);
    }
    private void OnSelectionChanged(object? sender, EventArgs e) => settingsWindow?.UpdateSelectionAvailability(Session.HasSelectedCue && !Session.IsProjectBusy && !Session.IsClosing);
    private void ApplyWindowPreferences()
    {
        var preferences = Session.Preferences;
        layouts.ApplyCulture(Session.InterfaceCulture);
        windowRegistry.UpdatePreferences(preferences);
        if (Avalonia.Application.Current is { } application)
        {
            application.RequestedThemeVariant = preferences.Theme switch
            {
                WorkbenchTheme.LIGHT => ThemeVariant.Light, WorkbenchTheme.DARK => ThemeVariant.Dark, _ => ThemeVariant.Default
            };
            application.Resources["SystemAccentColor"] = Color.Parse(preferences.AccentColor);
            if (application.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
            {
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    if (!fluent.Palettes.TryGetValue(variant, out var palette))
                    {
                        palette = new();
                        fluent.Palettes[variant] = palette;
                    }
                    palette.Accent = Color.Parse(preferences.AccentColor);
                }
            }
        }
        settingsWindow?.UpdatePreferences(preferences);
        ViewModel.Log.RefreshLanguage();
    }
}
