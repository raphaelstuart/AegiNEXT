using AegiNext.Application.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Fonts;
using AegiNext.Desktop.Settings;
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
    private Task? disposeTask;
    private int queuedStyles;
    private int queuedEffects;
    private bool closing;

    internal DesktopApplicationContext(WorkbenchPreferencesStore? preferencesStore = null,
        WorkbenchPreferences? initialPreferences = null)
    {
        PreferencesStore = preferencesStore ?? new(Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY"));
        preferences = initialPreferences ?? PreferencesStore.Load();
        preferences.Validate();
        StyleLibrary = new(Path.Combine(PreferencesStore.DirectoryPath, "subtitle-styles.aegistyles"));
        EffectScriptLibrary = new(Path.Combine(PreferencesStore.DirectoryPath, "effect-scripts.json"));
        RecentProjects = new(PreferencesStore.DirectoryPath);
        RecentProjects.ErrorChanged += OnRecentProjectsError;
        ApplyAppearance(preferences);
        LastError = PreferencesStore.LoadError ?? RecentProjects.LastError;
        var stylesInitialization = EnqueueLibraryOperation(() => StyleLibrary.LoadAsync(), true, false);
        var effectsInitialization = EnqueueLibraryOperation(() => EffectScriptLibrary.LoadAsync(), false, false);
        Initialization = Task.WhenAll(stylesInitialization, effectsInitialization);
    }

    internal event EventHandler? PreferencesChanged;
    internal event EventHandler? StylesChanged;
    internal event EventHandler? EffectsChanged;
    internal event EventHandler? BusyChanged;
    internal event EventHandler? ErrorChanged;
    internal WorkbenchPreferencesStore PreferencesStore { get; }
    internal SubtitleStylePresetLibrary StyleLibrary { get; }
    internal EffectScriptPresetLibrary EffectScriptLibrary { get; }
    internal RecentProjectService RecentProjects { get; }
    internal SubtitleFontSelectionService Fonts => fonts.Value;
    internal Task Initialization { get; }
    internal Exception? LastError { get; private set; }

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
                return Task.WhenAll(preferencesCompletion, stylesCompletion, effectsCompletion, RecentProjects.Completion);
            }
        }
    }

    internal bool StylesBusy => Volatile.Read(ref queuedStyles) > 0;
    internal bool EffectsBusy => Volatile.Read(ref queuedEffects) > 0;

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
        return EnqueueLibraryOperation(operation, true, true);
    }

    internal Task RunEffectOperationAsync(Func<Task> operation)
    {
        return EnqueueLibraryOperation(operation, false, true);
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

    private Task EnqueueLibraryOperation(Func<Task> operation, bool styles, bool propagateFailure)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Task previous;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (styles)
            {
                previous = stylesCompletion;
                stylesCompletion = completion.Task;
                queuedStyles++;
            }
            else
            {
                previous = effectsCompletion;
                effectsCompletion = completion.Task;
                queuedEffects++;
            }
        }

        BusyChanged?.Invoke(this, EventArgs.Empty);
        _ = ExecuteLibraryOperationAsync(previous, operation, styles, propagateFailure, completion, result);
        return result.Task;
    }

    private async Task ExecuteLibraryOperationAsync(Task previous, Func<Task> operation, bool styles,
        bool propagateFailure, TaskCompletionSource completion, TaskCompletionSource result)
    {
        Exception? failure = null;
        try
        {
            await previous;
            var before = styles ? (object)StyleLibrary.Snapshot : EffectScriptLibrary.Snapshot;
            await operation();
            var after = styles ? (object)StyleLibrary.Snapshot : EffectScriptLibrary.Snapshot;
            if (!ReferenceEquals(before, after))
            {
                if (styles)
                {
                    StylesChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    EffectsChanged?.Invoke(this, EventArgs.Empty);
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
            ReportError(error);
        }
        finally
        {
            if (styles)
            {
                Interlocked.Decrement(ref queuedStyles);
            }
            else
            {
                Interlocked.Decrement(ref queuedEffects);
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

    private async Task SavePreferencesAsync(Task previous, WorkbenchPreferences value, TaskCompletionSource completion)
    {
        try
        {
            await previous;
            await PreferencesStore.SaveAsync(value);
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
        PreferencesStore.Dispose();
        PreferencesChanged = null;
        StylesChanged = null;
        EffectsChanged = null;
        BusyChanged = null;
        ErrorChanged = null;
    }
}
