using AegiNext.Desktop.I18n;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Media.Encoding;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs,
    IWorkbenchExportService exportService) : IDisposable
{
    private AegiTaskHandle<VideoExportResult>? taskHandle;
    private bool activeOperation;
    private bool disposed;
    private long revision;
    private string? statusKey;
    private string? statusEncoder;
    internal bool IsChoosingOutput { get; private set; }
    internal bool IsRunning => session.ViewModel.Export.IsRunning;
    internal bool CanStart => !disposed && !activeOperation;
    internal bool CanCancel => taskHandle?.Snapshot is { CanCancel: true, State: AegiTaskState.Queued or AegiTaskState.Running };
    public Task Completion { get; private set; } = Task.CompletedTask;
    internal void Cancel() => taskHandle?.RequestCancel();

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
        activeOperation = true;
        revision++;
        IsChoosingOutput = true;
        session.ViewModel.RefreshCommands();
        Completion = ChooseAndRunAsync(request, session.Controller.Snapshot.Duration);
        return Completion;
    }

    private async Task ChooseAndRunAsync(VideoExportRequest request, MediaTime? duration)
    {
        try
        {
            var path = await dialogs.SaveFileAsync("Export", "Videos", ["*.mp4", "*.mkv"], ".mp4", session.ProjectDisplayName + ".mp4");
            if (path is null || session.IsClosing)
            {
                return;
            }

            IsChoosingOutput = false;
            var vm = session.ViewModel.Export;
            vm.IsRunning = true;
            vm.ProgressVisible = true;
            vm.ProgressIndeterminate = true;
            statusKey = "Tasks.Queued";
            statusEncoder = null;
            RefreshLanguage();
            taskHandle = session.ApplicationContext.Tasks.Submit(new VideoExportTask(session, this, request with { OutputPath = path }, duration));
            await taskHandle.Completion;
        }
        catch (OperationCanceledException)
        {
            statusKey = "Workbench.Cancelled";
            statusEncoder = null;
            RefreshLanguage();
            session.ViewModel.Export.ProgressVisible = false;
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
            session.ViewModel.Export.IsRunning = false;
            session.ViewModel.RefreshCommands();
        }
    }

    internal async Task<VideoExportResult> RunAsync(VideoExportRequest request, AegiTaskExecutionContext context, MediaTime? duration)
    {
        var token = context.CancellationToken;
        return await RunExportCoreAsync(request, context, duration, token);
    }

    private async Task<VideoExportResult> RunExportCoreAsync(VideoExportRequest request, AegiTaskExecutionContext context, MediaTime? duration, CancellationToken token)
    {
        var vm = session.ViewModel.Export;
        vm.IsRunning = true;
        vm.ProgressVisible = true;
        vm.ProgressIndeterminate = true;
        statusKey = null;
        statusEncoder = null;
        session.LogInfo("Export", Localization.Get("Workbench.Export"));
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
            context.ReportProgress(new("Tasks.Encoding", fraction, 1));
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
            var result = await exportService.ExportWithCommitAsync(request, progress, () => context.EnterCommit(), token);
            if (!session.IsClosing)
            {
                vm.ProgressIndeterminate = false;
                vm.Progress = 1;
                statusKey = "Workbench.Exported";
                statusEncoder = result.Encoder;
                RefreshLanguage();
                session.LogInfo("Export", vm.Status);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            statusKey = "Workbench.Cancelled";
            statusEncoder = null;
            RefreshLanguage();
            session.LogInfo("Export", vm.Status);
            vm.ProgressVisible = false;
            throw;
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
            throw;
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
        exportService.Dispose();
    }
}
