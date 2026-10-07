using AegiNext.Desktop.I18n;
using AegiNext.Media.Encoding;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs,
    IWorkbenchExportService exportService) : IDisposable
{
    private CancellationTokenSource? cancellation;
    private bool activeOperation;
    private bool disposed;
    private long revision;
    private string? statusKey;
    private string? statusEncoder;
    internal bool IsChoosingOutput { get; private set; }
    internal bool IsRunning => session.ViewModel.Export.IsRunning;
    internal bool CanStart => !disposed && !activeOperation;
    public Task Completion { get; private set; } = Task.CompletedTask;
    internal void Cancel() => cancellation?.Cancel();

    internal void RefreshLanguage()
    {
        if (statusKey is not null)
        {
            session.ViewModel.Export.Status = Localization.Get(statusKey) +
                (string.IsNullOrWhiteSpace(statusEncoder) ? string.Empty : " · " + statusEncoder);
        }
    }

    internal Task EncodeAsync()
    {
        if (!CanStart || session.IsProjectBusy || session.IsClosing || !session.TryCommitDrafts())
        {
            return Task.CompletedTask;
        }

        var snapshot = session.Editor.Snapshot;
        var directory = session.ProjectDirectory;
        var vm = session.ViewModel.Export;
        var request = new VideoExportRequest(snapshot, directory, string.Empty).WithSettings(vm.CaptureSettings());
        cancellation?.Dispose();
        cancellation = new();
        activeOperation = true;
        revision++;
        IsChoosingOutput = true;
        session.ViewModel.RefreshCommands();
        Completion = ChooseAndRunAsync(request, cancellation.Token);
        return Completion;
    }

    private async Task ChooseAndRunAsync(VideoExportRequest request, CancellationToken token)
    {
        try
        {
            var path = await dialogs.SaveFileAsync("Export", "Videos", ["*.mp4", "*.mkv"], ".mp4", session.ProjectDisplayName + ".mp4");
            if (path is null || session.IsClosing || token.IsCancellationRequested)
            {
                return;
            }

            IsChoosingOutput = false;
            await RunAsync(request with { OutputPath = path }, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!session.IsClosing)
            {
                session.ShowError(error);
            }
        }
        finally
        {
            IsChoosingOutput = false;
            activeOperation = false;
            session.ViewModel.RefreshCommands();
        }
    }

    private async Task RunAsync(VideoExportRequest request, CancellationToken token)
    {
        var vm = session.ViewModel.Export;
        vm.IsRunning = true;
        vm.ProgressVisible = true;
        vm.ProgressIndeterminate = true;
        statusKey = null;
        statusEncoder = null;
        session.LogInfo("Export", Localization.Get("Workbench.Export"));
        var duration = session.Controller.Snapshot.Duration;
        var exportRevision = revision;
        string? reportedEncoder = null;
        var progress = new Progress<VideoExportProgress>(value =>
        {
            if (session.IsClosing || token.IsCancellationRequested || !vm.IsRunning || exportRevision != revision)
            {
                return;
            }

            var fraction = value.Fraction ?? (duration is { } end && WorkbenchSession.ToSeconds(end) > 0
                ? WorkbenchSession.ToSeconds(value.Position) / WorkbenchSession.ToSeconds(end) : (double?)null);
            vm.ProgressIndeterminate = fraction is null;
            vm.Progress = fraction is { } known ? Math.Clamp(known, 0, 1) : 0;
            statusKey = null;
            statusEncoder = null;
            vm.Status = $"{WorkbenchSession.FormatTime(value.Position)} · {value.FrameCount}";
            if (!string.IsNullOrWhiteSpace(value.Encoder))
            {
                vm.Status += " · " + value.Encoder;
                if (reportedEncoder != value.Encoder)
                {
                    reportedEncoder = value.Encoder;
                    session.LogInfo("Export", Localization.Get("Workbench.Codec") + ": " + value.Encoder);
                }
            }
        });
        try
        {
            var result = await exportService.ExportAsync(request, progress, token);
            if (!session.IsClosing)
            {
                vm.ProgressIndeterminate = false;
                vm.Progress = 1;
                statusKey = "Workbench.Exported";
                statusEncoder = result.Encoder;
                RefreshLanguage();
                session.LogInfo("Export", vm.Status);
            }
        }
        catch (OperationCanceledException)
        {
            statusKey = "Workbench.Cancelled";
            statusEncoder = null;
            RefreshLanguage();
            session.LogInfo("Export", vm.Status);
            vm.ProgressVisible = false;
        }
        catch (Exception error)
        {
            if (!session.IsClosing)
            {
                session.ShowError(error);
                statusKey = null;
                statusEncoder = null;
                vm.Status = error.Message;
                vm.ProgressVisible = false;
            }
        }
        finally
        {
            vm.IsRunning = false;
            session.ViewModel.RefreshCommands();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        cancellation?.Dispose();
        exportService.Dispose();
    }
}
