using AegiNext.Application.Presets;
using AegiNext.Media.Encoding.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Fonts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Workspace;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace AegiNext.Desktop.Startup;

internal sealed class DesktopApplicationContext : IAsyncDisposable
{
    private readonly Lock lifetime = new();
    private readonly Lazy<SubtitleFontSelectionService> fonts = new(() => new(new SystemFontCatalog()));
    private WorkbenchPreferences preferences;
    private Task preferencesCompletion = Task.CompletedTask;
    private Task stylesCompletion = Task.CompletedTask;
    private Task effectsCompletion = Task.CompletedTask;
    private Task exportsCompletion = Task.CompletedTask;
    private Task? disposeTask;
    private int queuedStyles;
    private int queuedEffects;
    private int queuedExports;
    private bool closing;
    private Exception? preferencesLoadError;
    private readonly Dictionary<PersonalLibraryKind, Exception> libraryLoadErrors = [];

    internal DesktopApplicationContext(WorkbenchPreferencesStore? preferencesStore = null,
        WorkbenchPreferences? initialPreferences = null)
    {
        PreferencesStore = preferencesStore ?? new(Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY"));
        preferences = initialPreferences ?? PreferencesStore.Load();
        preferences.Validate();
        preferencesLoadError = PreferencesStore.LoadError;
        SettingsRestore = new(PreferencesStore.DirectoryPath);
        StyleLibrary = new(Path.Combine(PreferencesStore.DirectoryPath, "subtitle-styles.aegistyles"));
        EffectScriptLibrary = new(Path.Combine(PreferencesStore.DirectoryPath, "effect-scripts.json"));
        ExportPresetLibrary = new(Path.Combine(PreferencesStore.DirectoryPath, "export-presets.aegiexports"));
        RecentProjects = new(PreferencesStore.DirectoryPath);
        RecentProjects.ErrorChanged += OnRecentProjectsError;
        ApplyAppearance(preferences);
        LastError = SettingsRestoreStartup.GetError(PreferencesStore.DirectoryPath) ?? PreferencesStore.LoadError ?? RecentProjects.LastError;
        var stylesInitialization = EnqueueLibraryOperation(() => StyleLibrary.LoadAsync(), PersonalLibraryKind.STYLE, false);
        var effectsInitialization = EnqueueLibraryOperation(() => EffectScriptLibrary.LoadAsync(), PersonalLibraryKind.EFFECT, false);
        var exportsInitialization = EnqueueLibraryOperation(() => ExportPresetLibrary.LoadAsync(), PersonalLibraryKind.EXPORT, false);
        Initialization = Task.WhenAll(stylesInitialization, effectsInitialization, exportsInitialization);
    }

    internal event EventHandler? PreferencesChanged;
    internal event EventHandler? StylesChanged;
    internal event EventHandler? EffectsChanged;
    internal event EventHandler? ExportPresetsChanged;
    internal event EventHandler? BusyChanged;
    internal event EventHandler? ErrorChanged;
    internal WorkbenchPreferencesStore PreferencesStore { get; }
    internal SubtitleStylePresetLibrary StyleLibrary { get; }
    internal EffectScriptPresetLibrary EffectScriptLibrary { get; }
    internal VideoExportPresetLibrary ExportPresetLibrary { get; }
    internal UserSettingsRestoreService SettingsRestore { get; }
    internal RecentProjectService RecentProjects { get; }
    internal SubtitleFontSelectionService Fonts => fonts.Value;
    internal Task Initialization { get; }
    internal Exception? LastError { get; private set; }
    internal Exception? SettingsLoadError
    {
        get
        {
            lock (lifetime)
            {
                return preferencesLoadError ?? libraryLoadErrors.Values.FirstOrDefault();
            }
        }
    }

    internal WorkbenchPreferences Preferences
    {
        get
        {
            lock (lifetime)
            {
                return preferences;
            }
        }
    }

    internal Task Completion
    {
        get
        {
            lock (lifetime)
            {
                return Task.WhenAll(preferencesCompletion, stylesCompletion, effectsCompletion, exportsCompletion, RecentProjects.Completion);
            }
        }
    }

    internal bool StylesBusy => Volatile.Read(ref queuedStyles) > 0;
    internal bool EffectsBusy => Volatile.Read(ref queuedEffects) > 0;
    internal bool ExportPresetsBusy => Volatile.Read(ref queuedExports) > 0;

    internal void UpdatePreferences(Func<WorkbenchPreferences, WorkbenchPreferences> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        WorkbenchPreferences value;
        bool languageChanged;
        Task previous;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            value = update(preferences);
            ArgumentNullException.ThrowIfNull(value);
            value.Validate();
            if (preferences == value)
            {
                return;
            }

            languageChanged = !string.Equals(preferences.Language, value.Language, StringComparison.OrdinalIgnoreCase);
            preferences = value;
            previous = preferencesCompletion;
            preferencesCompletion = completion.Task;
        }

        try
        {
            ApplyAppearance(value, languageChanged);
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _ = SavePreferencesAsync(previous, value, completion);
        }
    }

    internal Task RunStyleOperationAsync(Func<Task> operation)
    {
        return EnqueueLibraryOperation(operation, PersonalLibraryKind.STYLE, true);
    }

    internal Task RunEffectOperationAsync(Func<Task> operation)
    {
        return EnqueueLibraryOperation(operation, PersonalLibraryKind.EFFECT, true);
    }

    internal Task RunExportPresetOperationAsync(Func<Task> operation)
    {
        return EnqueueLibraryOperation(operation, PersonalLibraryKind.EXPORT, true);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (lifetime)
        {
            closing = true;
            disposeTask ??= DisposeCoreAsync();
            return new(disposeTask);
        }
    }

    private Task EnqueueLibraryOperation(Func<Task> operation, PersonalLibraryKind kind, bool propagateFailure)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Task previous;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            switch (kind)
            {
                case PersonalLibraryKind.STYLE:
                    previous = stylesCompletion;
                    stylesCompletion = completion.Task;
                    queuedStyles++;
                    break;
                case PersonalLibraryKind.EFFECT:
                    previous = effectsCompletion;
                    effectsCompletion = completion.Task;
                    queuedEffects++;
                    break;
                case PersonalLibraryKind.EXPORT:
                    previous = exportsCompletion;
                    exportsCompletion = completion.Task;
                    queuedExports++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
        _ = ExecuteLibraryOperationAsync(previous, operation, kind, propagateFailure, completion, result);
        return result.Task;
    }

    private async Task ExecuteLibraryOperationAsync(Task previous, Func<Task> operation, PersonalLibraryKind kind,
        bool propagateFailure, TaskCompletionSource completion, TaskCompletionSource result)
    {
        Exception? failure = null;
        try
        {
            await previous;
            var before = LibrarySnapshot(kind);
            await operation();
            var after = LibrarySnapshot(kind);
            if (!propagateFailure || !ReferenceEquals(before, after))
            {
                SetLibraryLoadError(kind, null);
            }
            if (!ReferenceEquals(before, after))
            {
                switch (kind)
                {
                    case PersonalLibraryKind.STYLE:
                        StylesChanged?.Invoke(this, EventArgs.Empty);
                        break;
                    case PersonalLibraryKind.EFFECT:
                        EffectsChanged?.Invoke(this, EventArgs.Empty);
                        break;
                    case PersonalLibraryKind.EXPORT:
                        ExportPresetsChanged?.Invoke(this, EventArgs.Empty);
                        break;
                }
            }
        }
        catch (OperationCanceledException cancellation)
        {
            failure = cancellation;
        }
        catch (Exception error)
        {
            failure = error;
            if (!propagateFailure)
            {
                SetLibraryLoadError(kind, error);
            }
            ReportError(error);
        }
        finally
        {
            switch (kind)
            {
                case PersonalLibraryKind.STYLE:
                    Interlocked.Decrement(ref queuedStyles);
                    break;
                case PersonalLibraryKind.EFFECT:
                    Interlocked.Decrement(ref queuedEffects);
                    break;
                case PersonalLibraryKind.EXPORT:
                    Interlocked.Decrement(ref queuedExports);
                    break;
            }

            try
            {
                BusyChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                completion.TrySetResult();
            }
        }

        if (!propagateFailure || failure is null)
        {
            result.TrySetResult();
        }
        else if (failure is OperationCanceledException cancellation)
        {
            result.TrySetCanceled(cancellation.CancellationToken);
        }
        else
        {
            result.TrySetException(failure);
        }
    }

    private object LibrarySnapshot(PersonalLibraryKind kind)
    {
        return kind switch
        {
            PersonalLibraryKind.STYLE => StyleLibrary.Snapshot,
            PersonalLibraryKind.EFFECT => EffectScriptLibrary.Snapshot,
            PersonalLibraryKind.EXPORT => ExportPresetLibrary.Snapshot,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async Task SavePreferencesAsync(Task previous, WorkbenchPreferences value, TaskCompletionSource completion)
    {
        try
        {
            await previous;
            await PreferencesStore.SaveAsync(value);
            lock (lifetime)
            {
                preferencesLoadError = null;
            }
        }
        catch (Exception error)
        {
            ReportError(error);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private void SetLibraryLoadError(PersonalLibraryKind kind, Exception? error)
    {
        lock (lifetime)
        {
            if (error is null)
            {
                libraryLoadErrors.Remove(kind);
            }
            else
            {
                libraryLoadErrors[kind] = error;
            }
        }
    }

    private void ReportError(Exception error)
    {
        LastError = error;
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecentProjectsError(object? sender, EventArgs e)
    {
        if (RecentProjects.LastError is { } error)
        {
            ReportError(error);
        }
    }

    private static void ApplyAppearance(WorkbenchPreferences value, bool applyLanguage = true)
    {
        if (applyLanguage)
        {
            WorkbenchCompositionRoot.ApplyLanguagePreference(value.Language);
        }
        if (Avalonia.Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = value.Theme switch
        {
            WorkbenchTheme.LIGHT => ThemeVariant.Light,
            WorkbenchTheme.DARK => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        var accent = Color.Parse(value.AccentColor);
        application.Resources["SystemAccentColor"] = accent;
        if (application.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            if (!fluent.Palettes.TryGetValue(variant, out var palette))
            {
                palette = new();
                fluent.Palettes[variant] = palette;
            }

            palette.Accent = accent;
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Completion;
        RecentProjects.ErrorChanged -= OnRecentProjectsError;
        await RecentProjects.DisposeAsync();
        StyleLibrary.Dispose();
        EffectScriptLibrary.Dispose();
        ExportPresetLibrary.Dispose();
        SettingsRestore.Dispose();
        PreferencesStore.Dispose();
        PreferencesChanged = null;
        StylesChanged = null;
        EffectsChanged = null;
        ExportPresetsChanged = null;
        BusyChanged = null;
        ErrorChanged = null;
    }
}
