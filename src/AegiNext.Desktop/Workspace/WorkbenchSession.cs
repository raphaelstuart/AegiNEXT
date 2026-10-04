using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Application.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession : IAsyncDisposable
{
    private static readonly string[] blendKeys = ["Normal", "Multiply", "Screen", "AddBlend", "Overlay", "Darken", "Lighten", "Difference"];
    private static readonly string[] interpolationKeys = ["Hold", "Linear", "EaseIn", "EaseOut", "Smooth"];
    private static readonly string[] speedKeys = ["Fast", "Medium", "Slow"];
    private readonly ProjectEditor editor;
    private readonly VideoPreviewController controller;
    private readonly IWorkbenchDialogService dialogs;
    private readonly Func<Action, CancellationToken, Task> dispatch;
    private readonly WorkbenchPreferencesStore preferencesStore;
    private readonly SubtitleStylePresetLibrary styleLibrary;
    private readonly CultureInfo systemCulture;
    private readonly string scratchDirectory;
    private readonly Dictionary<Guid, int> textCarets = [];
    private readonly ProjectWorkflowCoordinator workflow;
    private readonly AnalysisCoordinator analysis;
    private readonly ExportCoordinator export;
    private readonly StyleLibraryCoordinator styles;
    private readonly LayerEditingCoordinator layerEditing;
    private readonly PlaybackSeekingCoordinator playback;
    private readonly PreviewFrameCatalog previewFrames = new();
    private ProjectPreviewState previewState = new(new(), Path.GetTempPath());
    private WorkbenchPreferences preferences;
    private Task preferencesWrite = Task.CompletedTask;
    private string? projectPath;
    private string projectDirectory;
    private Exception? previewRenderError;
    private bool updatingWorkbench;
    private bool projectBusy;
    private bool closing;
    private bool stylesDirty;
    private bool effectsDirty;
    private TaskCompletionSource projectIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? disposeTask;
    private TimingSession timingSession = new();

    internal WorkbenchSession(IWorkbenchDialogService dialogs,
        Func<Action<VideoPreviewUpdate>, VideoPreviewController>? controllerFactory = null,
        Func<Action, CancellationToken, Task>? dispatch = null,
        ProjectEditor? editor = null,
        WorkbenchPreferencesStore? preferencesStore = null,
        IWorkbenchExportService? exportService = null)
    {
        this.dialogs = dialogs;
        this.dispatch = dispatch ?? DispatchAsync;
        this.editor = editor ?? new();
        this.preferencesStore = preferencesStore ?? new(Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY"));
        preferences = this.preferencesStore.Load();
        systemCulture = CultureInfo.CurrentUICulture;
        scratchDirectory = Path.Combine(Path.GetTempPath(), "AegiNext", Guid.NewGuid().ToString("N"));
        projectDirectory = scratchDirectory;
        styleLibrary = new(Path.Combine(this.preferencesStore.DirectoryPath, "subtitle-styles.aegistyles"));
        ViewModel = new(this);
        controller = controllerFactory?.Invoke(ApplyUpdate) ?? new(this.dispatch, ApplyUpdate,
            () => new ProjectPreviewConverter(() => Volatile.Read(ref previewState),
                error => Volatile.Write(ref previewRenderError, error), previewFrames));
        workflow = new(this, dialogs);
        analysis = new(this);
        export = new(this, dialogs, exportService ?? new VideoWorkbenchExportService(new AegiNext.Media.Encoding.VideoExporter()));
        styles = new(this, dialogs);
        layerEditing = new(this, dialogs);
        playback = new(this, controller);
        this.editor.Changed += OnDocumentChanged;
        ViewModel.Styles.PropertyChanged += OnStylePropertyChanged;
        ViewModel.Effects.PropertyChanged += OnEffectPropertyChanged;
        ViewModel.Preview.PropertyChanged += OnPreviewPropertyChanged;
        ViewModel.Export.PropertyChanged += OnExportPropertyChanged;
        ApplyPreferences();
        RefreshDocument();
        styles.Initialize();
        if (this.preferencesStore.LoadError is { } error)
        {
            ShowError(error);
        }
    }

    internal event EventHandler<VideoPreviewUpdate>? PreviewUpdated;
    internal event EventHandler? PreferencesChanged;
    internal event EventHandler? StyleLibraryChanged;
    internal event EventHandler? SelectionChanged;
    internal event EventHandler? SubtitleScrollRequested;
    internal WorkbenchViewModel ViewModel { get; }
    internal Exception? LastError { get; private set; }
    internal ProjectEditor Editor => editor;
    internal VideoPreviewController Controller => controller;
    internal WorkbenchPreferences Preferences => preferences;
    internal WorkbenchPreferencesStore PreferencesStore => preferencesStore;
    internal SubtitleStylePresetLibrary StyleLibrary => styleLibrary;
    internal ProjectDocument DocumentSnapshot => editor.Snapshot;
    internal CultureInfo InterfaceCulture => preferences.Language == "system" ? systemCulture : CultureInfo.GetCultureInfo(preferences.Language);
    internal bool IsClosing => closing;
    internal bool IsProjectBusy => projectBusy;
    internal bool IsUpdating { get => updatingWorkbench; set => updatingWorkbench = value; }
    internal Guid? SelectedLayerId { get => SceneEditing.LayerId; set => SceneEditing.LayerId = value; }
    internal Guid? SelectedCueId { get => SceneEditing.CueId; set => SceneEditing.CueId = value; }
    internal MediaTime? SelectedKeyTime { get => SceneEditing.KeyframeTime; set => SceneEditing.KeyframeTime = value; }
    internal bool HasSelectedCue => SelectedCue is not null;
    internal string ProjectDirectory => projectDirectory;
    internal string? ProjectPath { get => projectPath; set => projectPath = value; }
    internal string ScratchDirectory => scratchDirectory;
    internal MediaTime ProjectPosition => (playback.PendingPosition ?? controller.Snapshot.Position) - (controller.Snapshot.Start ?? MediaTime.Zero);
    internal ProjectLayer? SelectedLayer => Flatten(editor.Snapshot.Layers).FirstOrDefault(value => value.Id == SelectedLayerId);
    internal SubtitleLine? SelectedCue => editor.Snapshot.Subtitles.FirstOrDefault(value => value.Id == SelectedCueId);
    internal AnalysisCoordinator Analysis => analysis;
    internal ExportCoordinator Export => export;
    internal StyleLibraryCoordinator Styles => styles;

    internal Task OpenMediaAsync(string path, bool updateProject) => workflow.OpenMediaAsync(path, updateProject);
    internal Task RequestSettingsAsync(SettingsPage? page = null)
    {
        return ViewModel.RequestHostCommandAsync(WorkbenchCommand.OPEN_SETTINGS, page);
    }

    internal void UpdatePreferences(WorkbenchPreferences value)
    {
        value.Validate();
        preferences = value;
        ApplyPreferences();
        QueuePreferencesWrite();
    }

    internal void ShowError(Exception error, bool recordLog = true)
    {
        if (recordLog)
        {
            LogError("Workspace", error);
        }
        LastError = error;
        var text = error.Message;
        ViewModel.Error = text.Length > 700 ? text[..700] + "…" : text;
        ViewModel.RefreshCommands();
    }

    internal async Task RunCommandAsync(Func<Task> command)
    {
        if (closing)
        {
            return;
        }

        try
        {
            LastError = null;
            ViewModel.Error = null;
            await command();
        }
        catch (OperationCanceledException)
        {
            LogInfo("Workflow", WorkbenchText.Get("Cancelled"));
        }
        catch (Exception error)
        {
            if (!closing)
            {
                ShowError(error);
            }
        }
        finally
        {
            if (!closing)
            {
                Tick();
                RestorePlacementDiagnostic();
                ViewModel.RefreshCommands();
            }
        }
    }

    internal Task EditAsync(Action action)
    {
        if (!updatingWorkbench && !projectBusy && !closing && TryCommitDrafts())
        {
            InvalidateTimingSession();
            action();
        }

        return Task.CompletedTask;
    }

    internal void SetProjectBusy(bool value)
    {
        if (value && !projectBusy)
        {
            playback.Invalidate();
            ViewModel.CancelGestures();
            projectIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        projectBusy = value;
        ViewModel.IsBusy = value;
        ViewModel.RefreshCommands();
        if (!value)
        {
            projectIdle.TrySetResult();
        }
    }

    internal async Task WaitForProjectIdleAsync()
    {
        while (projectBusy)
        {
            await projectIdle.Task;
        }
    }

    internal async Task<bool> RequestCloseAsync(Func<Task>? beforeDispose = null)
    {
        if (closing)
        {
            return false;
        }

        await WaitForProjectIdleAsync();
        if (!await workflow.ConfirmDiscardOrSaveAsync())
        {
            return false;
        }

        closing = true;
        ViewModel.CancelGestures();
        ViewModel.RefreshCommands();
        if (beforeDispose is not null)
        {
            try
            {
                await beforeDispose();
            }
            catch
            {
                closing = false;
                ViewModel.RefreshCommands();
                throw;
            }
        }
        await DisposeAsync();
        return true;
    }

    /// <summary>等待会话任务结束并释放资源；重复调用等待同一次释放。</summary>
    public ValueTask DisposeAsync()
    {
        disposeTask ??= DisposeCoreAsync();
        return new(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        closing = true;
        editor.Changed -= OnDocumentChanged;
        ViewModel.CancelGestures();
        analysis.Cancel();
        export.Cancel();
        try
        {
            await Task.WhenAll(analysis.Completion, export.Completion, preferencesWrite, styles.Completion);
        }
        finally
        {
            await controller.DisposeAsync();
            previewFrames.Clear();
            layerPlacement.Dispose();
            analysis.Dispose();
            export.Dispose();
            styleLibrary.Dispose();
            preferencesStore.Dispose();
            DisposeJournal();
            PreviewUpdated = null;
            if (Directory.Exists(scratchDirectory))
            {
                Directory.Delete(scratchDirectory, true);
            }
        }
    }

    private void ApplyPreferences()
    {
        CultureInfo.CurrentUICulture = InterfaceCulture;
        CultureInfo.DefaultThreadCurrentUICulture = InterfaceCulture;
        var previous = updatingWorkbench;
        updatingWorkbench = true;
        try
        {
            ViewModel.Preview.Volume = preferences.Volume;
            controller.SetVolume(preferences.Volume);
            ViewModel.Preview.EmptyLabel = PreviewText.Get("Empty", InterfaceCulture);
            ViewModel.Styles.Alignments = alignments.Select(value => SettingsText.Get(value.ToString())).ToArray();
            ViewModel.Effects.Blends = blendKeys.Select(WorkbenchText.Get).ToArray();
            ViewModel.Effects.Properties = Enum.GetValues<AnimationProperty>().Select(WorkbenchText.Property).ToArray();
            ViewModel.Effects.Interpolations = interpolationKeys.Select(WorkbenchText.Get).ToArray();
            ViewModel.Export.RefreshChoices([WorkbenchText.Get("Automatic"), "H.264", "HEVC / H.265"],
                speedKeys.Select(WorkbenchText.Get).ToArray(),
                [WorkbenchText.Get("Copy"), "AAC", WorkbenchText.Get("NoAudio")]);
            RefreshDocument();
            Tick();
        }
        finally
        {
            updatingWorkbench = previous;
        }

        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueuePreferencesWrite()
    {
        var previous = preferencesWrite;
        var value = preferences;
        preferencesWrite = SavePreferencesAsync(previous, value);
    }

    private async Task SavePreferencesAsync(Task previous, WorkbenchPreferences value)
    {
        await previous;
        try
        {
            await preferencesStore.SaveAsync(value);
        }
        catch (Exception error)
        {
            if (!closing)
            {
                ShowError(error);
            }
        }
    }

    private void ApplyUpdate(VideoPreviewUpdate update)
    {
        if (!closing)
        {
            if (update.ClearFrame)
            {
                ViewModel.Preview.HasFrame = false;
            }
            else if (update.Frame is not null)
            {
                ViewModel.Preview.HasFrame = true;
            }

            var presented = update.Frame is { } frame
                ? update with { BackgroundFrame = update.BackgroundFrame ?? previewFrames.FindBackground(frame) ?? frame }
                : update;
            PreviewUpdated?.Invoke(this, presented);
            Tick();
        }
    }

    internal void Tick()
    {
        var snapshot = controller.Snapshot;
        var preview = ViewModel.Preview;
        var relative = ProjectPosition;
        preview.FileTitle = snapshot.FilePath is { } path ? Path.GetFileName(path) : PreviewText.Get("Preview", InterfaceCulture);
        preview.IsOpening = snapshot.IsOpening;
        preview.CanPlay = !closing && snapshot.Error is null && snapshot.State is VideoPlaybackState.PAUSED or VideoPlaybackState.PLAYING or VideoPlaybackState.ENDED;
        preview.PlayLabel = PreviewText.Get(snapshot.State == VideoPlaybackState.PLAYING ? "Pause" : "Play", InterfaceCulture);
        preview.CanSeek = preview.CanPlay && snapshot.Duration is { } duration && duration > MediaTime.Zero;
        preview.Duration = snapshot.Duration is { } known ? Math.Max(0.001, ToSeconds(known)) : 1;
        if (!preview.IsScrubbing)
        {
            preview.Position = Math.Clamp(ToSeconds(relative), 0, preview.Duration);
        }

        preview.TimeLabel = $"{FormatTime(relative)} / {(snapshot.Duration is { } end ? FormatTime(end) : "--:--")}";
        var timeline = ViewModel.Timeline;
        timeline.MediaDuration = snapshot.Duration is { } mediaDuration ? ToSeconds(mediaDuration) : 0;
        timeline.Position = relative;
        ViewModel.Effects.Position = EditingPosition;
        RefreshAnimatedInspectorAtTime();
        RefreshEditingTargetLabel();
        RefreshEditingPreview();
        layerEditing.RefreshKeyframeAvailability();
        var durationSeconds = Math.Max(snapshot.Duration is { } value ? ToSeconds(value) : 60,
            editor.Snapshot.Subtitles.Select(line => ToSeconds(line.End)).DefaultIfEmpty(60).Max());
        timeline.ScrollMaximum = Math.Max(0, durationSeconds - timeline.VisibleDuration);
        timeline.ViewportSize = Math.Max(1, timeline.VisibleDuration);
        if (playback.PendingPosition is null && !timeline.IsSeeking && snapshot.State == VideoPlaybackState.PLAYING &&
            (ToSeconds(relative) < timeline.ViewStart || ToSeconds(relative) > timeline.ViewStart + timeline.VisibleDuration))
        {
            timeline.ViewStart = Math.Max(0, ToSeconds(relative) - timeline.VisibleDuration / 5);
        }

        var renderError = Volatile.Read(ref previewRenderError);
        SetDiagnosticError("Video playback", snapshot.Error);
        SetDiagnosticError("Audio playback", snapshot.AudioError);
        SetDiagnosticError("Preview rendering", renderError);
        if ((snapshot.Error ?? snapshot.AudioError ?? renderError) is { } error)
        {
            ShowError(error, false);
        }

        ViewModel.RefreshCommands();
    }

    private async void OnDocumentChanged(object? sender, EventArgs e)
    {
        RefreshDocument();
        if (projectBusy || closing)
        {
            return;
        }

        await RunCommandAsync(async () =>
        {
            var document = editor.Snapshot;
            var path = document.Media is { } binding
                ? ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId), projectDirectory)
                : null;
            if (!string.Equals(path, controller.Snapshot.FilePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                await workflow.SynchronizePreviewBindingAsync();
            }
            else if (controller.Snapshot.State == VideoPlaybackState.PAUSED)
            {
                await controller.SeekAsync(playback.PendingPosition ?? controller.Snapshot.Position);
            }
        });
    }

    internal void SetProjectLocation(string? path, string directory)
    {
        projectPath = path;
        projectDirectory = directory;
    }

    internal void ResetSelection()
    {
        SelectedCueId = null;
        SelectedLayerId = null;
        SelectedKeyTime = null;
        timingSession = timingSession.Reset();
        stylesDirty = false;
        effectsDirty = false;
        SceneEditing.DraftTarget = null;
        SceneEditing.GestureTarget = null;
        changedEffectFields.Clear();
    }

    internal static IEnumerable<ProjectLayer> Flatten(ImmutableArray<ProjectLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
            {
                yield return child;
            }
        }
    }

    internal static double ToSeconds(MediaTime time) => (double)time.Numerator / time.Denominator;
    internal static string FormatTime(MediaTime time)
    {
        var milliseconds = Math.Max(0, time.ToTimeSpan(MediaTimeRounding.FLOOR).Ticks / TimeSpan.TicksPerMillisecond);
        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 3_600_000:00}:{milliseconds / 60_000 % 60:00}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}");
    }

    private static async Task DispatchAsync(Action action, CancellationToken token) => await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Render, token);
}
