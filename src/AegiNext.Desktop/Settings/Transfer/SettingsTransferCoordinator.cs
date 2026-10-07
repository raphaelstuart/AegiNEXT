using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using Avalonia.Threading;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class SettingsTransferCoordinator : IDisposable
{
    private readonly DesktopApplicationContext applicationContext;
    private readonly SettingsWindow window;
    private readonly IWorkbenchDialogService dialogs;
    private readonly Func<WorkspaceLayoutFile>? captureLayout;
    private readonly Action? requestApplicationExit;
    private readonly CancellationTokenSource cancellation = new();
    private UserSettingsBundle? preview;
    private bool activeOperation;
    private bool disposed;

    internal SettingsTransferCoordinator(DesktopApplicationContext applicationContext, SettingsWindow window,
        IWorkbenchDialogService dialogs, Func<WorkspaceLayoutFile>? captureLayout, Action? requestApplicationExit)
    {
        this.applicationContext = applicationContext;
        this.window = window;
        this.dialogs = dialogs;
        this.captureLayout = captureLayout;
        this.requestApplicationExit = requestApplicationExit;
        var viewModel = window.ViewModel.Transfer;
        viewModel.ExportRequested += OnExportRequested;
        viewModel.ImportRequested += OnImportRequested;
        viewModel.StageRestoreRequested += OnStageRestoreRequested;
        viewModel.CancelPendingRequested += OnCancelPendingRequested;
        viewModel.RestartRequested += OnRestartRequested;
        applicationContext.SettingsRestore.PendingChanged += OnPendingChanged;
        applicationContext.BusyChanged += OnBusyChanged;
        RefreshPending();
        RefreshAvailability();
    }

    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>取消窗口请求并解除共享恢复状态订阅，阻止迟到的选择器结果。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation.Cancel();
        var viewModel = window.ViewModel.Transfer;
        viewModel.ExportRequested -= OnExportRequested;
        viewModel.ImportRequested -= OnImportRequested;
        viewModel.StageRestoreRequested -= OnStageRestoreRequested;
        viewModel.CancelPendingRequested -= OnCancelPendingRequested;
        viewModel.RestartRequested -= OnRestartRequested;
        applicationContext.SettingsRestore.PendingChanged -= OnPendingChanged;
        applicationContext.BusyChanged -= OnBusyChanged;
        cancellation.Dispose();
    }

    private void OnExportRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            await applicationContext.Initialization.WaitAsync(token);
            await applicationContext.Completion.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (applicationContext.SettingsLoadError is { } error)
            {
                throw new InvalidOperationException(Localization.Get("Settings.TransferExportUnavailable"), error);
            }

            var bundle = new UserSettingsBundle
            {
                Preferences = applicationContext.Preferences,
                Styles = applicationContext.StyleLibrary.Snapshot,
                Effects = applicationContext.EffectScriptLibrary.Snapshot,
                ExportPresets = applicationContext.ExportPresetLibrary.Snapshot,
                Layouts = captureLayout?.Invoke() ?? new WorkspaceLayoutStore(applicationContext.PreferencesStore.DirectoryPath).LoadStrict()
            };
            _ = UserSettingsBundleStore.Serialize(bundle);
            var path = await dialogs.SaveFileAsync("ExportUserSettings", "UserSettingsFiles", ["*.aegisettings"],
                ".aegisettings", "settings.aegisettings").WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (path is not null)
            {
                ValidateExportDestination(path);
                await UserSettingsBundleStore.SaveAsync(bundle, path, token);
                if (!disposed)
                {
                    window.ViewModel.Transfer.ShowStatus("Settings.TransferExported");
                }
            }
        });
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            var path = await dialogs.OpenFileAsync("ImportUserSettings", "UserSettingsFiles", ["*.aegisettings"]).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (path is null)
            {
                return;
            }

            var imported = await UserSettingsBundleStore.LoadAsync(path, token);
            token.ThrowIfCancellationRequested();
            if (!disposed)
            {
                preview = imported;
                window.ViewModel.Transfer.SetPreview(Path.GetFileName(path), imported.Styles.Presets.Length,
                    imported.Effects.Presets.Length, imported.ExportPresets.Presets.Length, imported.Layouts.Presets.Count);
            }
        });
    }

    private void ValidateExportDestination(string path)
    {
        var directory = Path.GetFullPath(applicationContext.PreferencesStore.DirectoryPath);
        var target = Path.GetFullPath(path);
        if (IsWithinDirectory(target, directory) || IsWithinDirectory(UserSettingsTransferFiles.ResolvePhysicalPath(target),
                UserSettingsTransferFiles.ResolvePhysicalPath(directory)))
        {
            throw new InvalidOperationException(Localization.Get("Settings.TransferUnsafeDestination"));
        }
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(path, directory, comparison) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, comparison);
    }

    private void OnStageRestoreRequested(object? sender, EventArgs e)
    {
        if (preview is not { } imported)
        {
            return;
        }

        var keepWorkspaceRoot = window.ViewModel.Transfer.KeepWorkspaceRoot;
        _ = RunAsync(async token =>
        {
            await applicationContext.Initialization.WaitAsync(token);
            var adapted = keepWorkspaceRoot ? imported with
            {
                Preferences = imported.Preferences with
                {
                    Projects = imported.Preferences.Projects with
                    {
                        WorkspaceRoot = applicationContext.Preferences.Projects.WorkspaceRoot
                    }
                }
            } : imported;
            adapted.Preferences.Validate();
            await applicationContext.SettingsRestore.StageAsync(adapted, token);
            if (!disposed)
            {
                preview = null;
                window.ViewModel.Transfer.ClearPreview();
                window.ViewModel.Transfer.ShowStatus(null);
                RefreshPending();
                await ConfirmRestartAsync(token);
            }
        });
    }

    private void OnCancelPendingRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            await applicationContext.SettingsRestore.CancelPendingAsync(token);
            if (!disposed)
            {
                RefreshPending();
                window.ViewModel.Transfer.ShowStatus("Settings.TransferCancelled");
            }
        });
    }

    private void OnRestartRequested(object? sender, EventArgs e)
    {
        if (applicationContext.SettingsRestore.HasPending)
        {
            _ = RunAsync(ConfirmRestartAsync);
        }
    }

    private async Task ConfirmRestartAsync(CancellationToken token)
    {
        if (requestApplicationExit is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmSettingsRestartAsync(token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (confirmed && !disposed)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && !token.IsCancellationRequested)
                {
                    requestApplicationExit();
                }
            });
        }
    }

    private void OnPendingChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshPending();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshPending);
        }
    }

    private void RefreshPending()
    {
        if (!disposed)
        {
            window.ViewModel.Transfer.UpdatePending(applicationContext.SettingsRestore.HasPending, requestApplicationExit is not null);
        }
    }

    private void OnBusyChanged(object? sender, EventArgs e)
    {
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        if (!disposed)
        {
            window.ViewModel.Transfer.IsBusy = activeOperation || applicationContext.StylesBusy ||
                applicationContext.EffectsBusy || applicationContext.ExportPresetsBusy;
        }
    }

    private Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (disposed || activeOperation)
        {
            return Task.CompletedTask;
        }

        activeOperation = true;
        RefreshAvailability();
        window.ShowError(null);
        window.ViewModel.Transfer.ShowError(null);
        window.ViewModel.Transfer.ShowStatus(null);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Completion = completion.Task;
        return ExecuteAsync(operation, completion, cancellation.Token);
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation, TaskCompletionSource completion,
        CancellationToken token)
    {
        try
        {
            await operation(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!disposed)
            {
                window.ViewModel.Transfer.ShowError(error.Message);
            }
        }
        finally
        {
            activeOperation = false;
            try
            {
                RefreshAvailability();
            }
            finally
            {
                completion.TrySetResult();
            }
        }
    }
}
