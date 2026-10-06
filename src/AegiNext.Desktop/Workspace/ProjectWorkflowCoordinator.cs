using System.Collections.Immutable;
using System.Text;
using AegiNext.Application;
using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectWorkflowCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private const int MAX_SUBTITLE_FILE_BYTES = 16 * 1024 * 1024;
    private UnavailableProjectMediaBinding? unavailableMediaBinding;
    internal bool IsNewProjectDialogOpen { get; private set; }
    internal async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (!session.TryCommitDrafts())
        {
            return false;
        }

        if (!session.Editor.HasUnsavedChanges)
        {
            return true;
        }

        var result = await dialogs.ConfirmUnsavedAsync();
        return result == 2 || result == 1 && await SaveProjectAsync(false);
    }

    internal async Task NewProjectAsync()
    {
        if (session.IsProjectBusy || session.IsClosing || IsNewProjectDialogOpen)
        {
            return;
        }
        IsNewProjectDialogOpen = true;
        session.ViewModel.RefreshCommands();
        try
        {
            await using var pause = await session.Persistence.PauseAsync();
            if (!await ConfirmDiscardOrSaveAsync() || session.IsClosing)
            {
                return;
            }
            await dialogs.ShowNewProjectAsync(session.Preferences.Projects.WorkspaceRoot, async (request, cancellationToken) =>
            {
                var result = await CreateProjectAsync(request, cancellationToken);
                foreach (var diagnostic in result.Diagnostics)
                {
                    session.ShowError(diagnostic);
                }
                return result;
            }, session.ProjectOperationsToken);
        }
        finally
        {
            IsNewProjectDialogOpen = false;
            session.ViewModel.RefreshCommands();
        }
    }

    internal async Task<ProjectOpenResult> CreateProjectAsync(ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        await using var pause = await session.Persistence.PauseAsync();
        return await session.Persistence.RunExclusiveAsync(() => CreateProjectCoreAsync(
            ProjectCreationService.GetProjectPath(request), request, cancellationToken));
    }

    internal async Task<ProjectOpenResult> CreateProjectAsync(string path,
        CancellationToken cancellationToken = default)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        await using var pause = await session.Persistence.PauseAsync();
        return await session.Persistence.RunExclusiveAsync(() => CreateProjectCoreAsync(path, null, cancellationToken));
    }

    private async Task<ProjectOpenResult> CreateProjectCoreAsync(string path, ProjectCreationRequest? request,
        CancellationToken cancellationToken)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }

        var previousDocument = session.Editor.Snapshot;
        var previousPreview = session.Controller.Snapshot;
        var previewChanged = false;
        var committed = false;
        session.SetProjectBusy(true);
        try
        {
            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path)!;
            if (request is not null)
            {
                ProjectCreationService.Validate(request);
            }
            previewChanged = true;
            await session.Controller.CloseMediaAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (session.IsClosing)
            {
                throw new OperationCanceledException();
            }

            ProjectDocument document;
            if (request is not null)
            {
                var created = await ProjectCreationService.CreateAsync(request, cancellationToken);
                path = created.Path;
                document = created.Document;
            }
            else
            {
                document = new() { Name = Path.GetFileNameWithoutExtension(path) };
                await ProjectStore.CreateAsync(document, path, cancellationToken);
            }
            session.SetProjectLocation(path, directory);
            session.ResetSelection();
            session.Editor.Reset(document);
            session.ActivateProjectPersistence();
            unavailableMediaBinding = null;
            committed = true;
            await session.ApplicationContext.RecentProjects.RecordAsync(path);
            var diagnostics = new List<Exception>();
            try
            {
                await session.Analysis.ClearAsync();
            }
            catch (Exception error)
            {
                diagnostics.Add(error);
            }

            session.LogInfo("Project", Localization.Get("WorkflowLog.ProjectCreated"), path);
            return new(ProjectOpenStatus.OPENED, diagnostics: diagnostics.AsReadOnly());
        }
        catch (Exception error)
        {
            if (committed)
            {
                return new(ProjectOpenStatus.OPENED, diagnostics: [error]);
            }

            if (previewChanged)
            {
                try
                {
                    await RestorePreviewAsync(previousDocument, previousPreview, error);
                }
                catch (Exception recoveryError)
                {
                    return new(ProjectOpenStatus.FAILED, recoveryError);
                }
            }
            return error is OperationCanceledException
                ? new(ProjectOpenStatus.CANCELLED)
                : new(ProjectOpenStatus.FAILED, error);
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task OpenProjectAsync()
    {
        if (session.IsProjectBusy)
        {
            return;
        }

        await using var pickerPause = await session.Persistence.PauseAsync();
        var path = await dialogs.OpenFileAsync("OpenProject", "Projects", ["*.aeginext"]);
        if (path is null)
        {
            return;
        }

        var result = await OpenProjectAsync(path);
        if (result.Error is { } error)
        {
            session.ShowError(error);
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            session.ShowError(diagnostic);
        }
    }

    internal async Task<ProjectOpenResult> OpenProjectAsync(string path,
        CancellationToken cancellationToken = default)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }

        await using var pause = await session.Persistence.PauseAsync();
        try
        {
            path = Path.GetFullPath(path);
            if (ProjectBackupStore.IsBackupPath(path))
            {
                return new(ProjectOpenStatus.FAILED, new InvalidDataException(Localization.Get("Workbench.BackupRestoreRequired")));
            }
            if (!await ConfirmDiscardOrSaveAsync() || session.IsProjectBusy || session.IsClosing ||
                cancellationToken.IsCancellationRequested)
            {
                return new(ProjectOpenStatus.CANCELLED);
            }
        }
        catch (OperationCanceledException)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        catch (Exception error)
        {
            return new(ProjectOpenStatus.FAILED, error);
        }

        return await session.Persistence.RunExclusiveAsync(() => OpenProjectCoreAsync(path, cancellationToken));
    }

    private async Task<ProjectOpenResult> OpenProjectCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        session.SetProjectBusy(true);
        var previousDocument = session.Editor.Snapshot;
        var previousPreview = session.Controller.Snapshot;
        var previewChanged = false;
        Exception? unavailableMediaError = null;
        var committed = false;
        try
        {
            var document = await ProjectStore.LoadAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(path)!;
            string? mediaPath = null;
            UnavailableProjectMediaBinding? deferredBinding = null;
            if (document.Media is not { } binding)
            {
                previewChanged = true;
                await session.Controller.CloseMediaAsync();
            }
            else
            {
                var asset = document.Assets.Single(asset => asset.Id == binding.AssetId);
                try
                {
                    mediaPath = ProjectAssetLocation.Resolve(asset, directory);
                    previewChanged = true;
                    await session.Controller.OpenAsync(mediaPath, cancellationToken);
                    if (session.Controller.Snapshot.Error is { } error)
                    {
                        throw error;
                    }

                    if (session.Controller.MediaInfo is null)
                    {
                        throw new InvalidDataException("媒体信息不可用。");
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException &&
                    !cancellationToken.IsCancellationRequested && !session.IsClosing)
                {
                    unavailableMediaError = error;
                    var reference = mediaPath ?? asset.ExternalPath ?? asset.RelativePath;
                    var proceed = await dialogs.ConfirmUnavailableMediaAsync(reference, error.Message, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!proceed || session.IsClosing)
                    {
                        throw new OperationCanceledException();
                    }

                    previewChanged = true;
                    await session.Controller.CloseMediaAsync();
                    mediaPath = null;
                    deferredBinding = new(binding, reference);
                }

                if (deferredBinding is null)
                {
                    ProjectMediaBindingValidator.Validate(document, session.Controller.MediaInfo!);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (session.IsClosing)
            {
                throw new OperationCanceledException();
            }

            session.SetProjectLocation(path, directory);
            session.ResetSelection();
            session.Editor.Reset(document);
            session.ActivateProjectPersistence();
            unavailableMediaBinding = deferredBinding;
            committed = true;
            await session.ApplicationContext.RecentProjects.RecordAsync(path);
            var diagnostics = new List<Exception>();
            if (mediaPath is not null)
            {
                try
                {
                    await session.Controller.SeekAsync(session.Controller.Snapshot.Start ?? MediaTime.Zero);
                    if (session.Controller.Snapshot.Error is { } previewError)
                    {
                        diagnostics.Add(previewError);
                    }
                }
                catch (Exception error)
                {
                    diagnostics.Add(error);
                }

                try
                {
                    await session.Analysis.StartAsync(mediaPath);
                }
                catch (Exception error)
                {
                    diagnostics.Add(error);
                }
            }
            else
            {
                try
                {
                    await session.Analysis.ClearAsync();
                }
                catch (Exception error)
                {
                    diagnostics.Add(error);
                }
            }
            session.LogInfo("Project", Localization.Get("WorkflowLog.ProjectOpened"), path);
            return new(ProjectOpenStatus.OPENED, diagnostics: diagnostics.AsReadOnly());
        }
        catch (Exception error)
        {
            if (!committed)
            {
                if (previewChanged)
                {
                    try
                    {
                        await RestorePreviewAsync(previousDocument, previousPreview, error);
                    }
                    catch (Exception recoveryError)
                    {
                        return new(ProjectOpenStatus.FAILED, recoveryError);
                    }
                }

                return error is OperationCanceledException
                    ? new(ProjectOpenStatus.CANCELLED)
                    : new(ProjectOpenStatus.FAILED, error);
            }

            return new(ProjectOpenStatus.OPENED, diagnostics: [error]);
        }
        finally
        {
            if (unavailableMediaError is { } error && !ReferenceEquals(session.Controller.Snapshot.Error, error))
            {
                session.DismissError(error);
            }
            session.SetProjectBusy(false);
        }
    }

    internal async Task OpenMediaAsync(string path, bool updateProject)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }

        path = ProjectAssetLocation.ResolveInputPath(path, session.ProjectDirectory);
        session.SetProjectBusy(true);
        var previousDocument = session.Editor.Snapshot;
        var previousPreview = session.Controller.Snapshot;
        var committed = false;
        try
        {
            session.ResetTiming();
            await session.Controller.OpenAsync(path);
            if (session.Controller.Snapshot.Error is { } error)
            {
                throw error;
            }

            var media = session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。");
            if (updateProject)
            {
                var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.MEDIA, session.ProjectDirectory);
                session.Editor.Apply("Bind media", document => document with
                {
                    Width = media.VideoWidth ?? document.Width, Height = media.VideoHeight ?? document.Height,
                    FrameRate = media.FrameRate ?? document.FrameRate,
                    Assets = document.Assets.Where(value => value.Kind != ProjectAssetKind.MEDIA).Append(asset)
                        .ToImmutableArray(),
                    Media = new(asset.Id, media.VideoStreamIndex, media.AudioStreamIndex, media.Start ?? MediaTime.Zero)
                });
                committed = true;
                await session.Controller.SeekAsync(media.Start ?? MediaTime.Zero);
            }

            await session.Analysis.StartAsync(path);
            session.LogInfo("Media", Localization.Get("WorkflowLog.MediaOpened"), path);
        }
        catch (Exception error)
        {
            if (!committed)
            {
                await RestorePreviewAsync(previousDocument, previousPreview, error);
            }

            throw;
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    private async Task RestorePreviewAsync(ProjectDocument document, VideoPreviewSnapshot previousPreview,
        Exception originalError)
    {
        try
        {
            if (previousPreview.FilePath is not { } path)
            {
                await session.Controller.CloseMediaAsync();
                return;
            }

            await session.Controller.OpenAsync(path);
            if (session.Controller.Snapshot.Error is { } error)
            {
                throw error;
            }

            if (document.Media is not null)
            {
                ProjectMediaBindingValidator.Validate(document,
                    session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。"));
            }
            await session.Controller.SeekAsync(previousPreview.Position);
        }
        catch (Exception recoveryError)
        {
            try
            {
                await ClearPreviewBindingAsync();
            }
            catch (Exception closeError)
            {
                throw new AggregateException(originalError, recoveryError, closeError);
            }

            throw new AggregateException(originalError, recoveryError);
        }
    }

    internal async Task<bool> SaveProjectAsync(bool saveAs)
    {
        if (session.IsProjectBusy || session.IsClosing)
        {
            return false;
        }
        await using var pause = await session.Persistence.PauseAsync();
        if (!session.TryCommitDrafts())
        {
            return false;
        }

        var destination = session.ProjectPath;
        if (saveAs || destination is null)
        {
            destination = await dialogs.SaveFileAsync("Save", "Projects", ["*.aeginext"], ".aeginext",
                session.ProjectDisplayName + ".aeginext");
            if (destination is null)
            {
                return false;
            }
        }

        if (session.IsClosing)
        {
            return false;
        }
        return await session.Persistence.RunExclusiveAsync(() => SaveProjectCoreAsync(destination));
    }

    private async Task<bool> SaveProjectCoreAsync(string destination)
    {
        session.SetProjectBusy(true);
        try
        {
            destination = Path.GetFullPath(destination);
            var locationChanged = !WorkbenchSession.PathsEqual(destination, session.ProjectPath);
            var snapshot = session.Editor.Snapshot;
            var directory = Path.GetDirectoryName(destination)!;
            var sameDirectory = WorkbenchSession.PathsEqual(directory, session.ProjectDirectory);
            var prepared = sameDirectory
                ? ProjectResources.NormalizeMediaReferences(snapshot, directory)
                : await ProjectResources.RebaseAsync(snapshot, session.ProjectDirectory, directory);
            await ProjectStore.SaveAsync(prepared, destination);
            session.SetProjectLocation(destination, directory);
            if (sameDirectory)
            {
                session.Editor.MarkSaved(snapshot, prepared);
            }
            else
            {
                session.Editor.Reset(prepared);
            }

            if (locationChanged)
            {
                session.ActivateProjectPersistence();
            }
            await session.Persistence.RecordManualSaveAsync(prepared);
            session.LogInfo("Project", Localization.Get("Workbench.Saved"), destination);
            await session.ApplicationContext.RecentProjects.RecordAsync(destination);
            return true;
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task ImportSubtitlesAsync(bool ass = false)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var path = await dialogs.OpenFileAsync("Import", "SubtitleFiles", ass ? ["*.ass"] : ["*.srt"]);
        if (path is null || session.IsClosing)
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var trackId = session.CurrentTrackId;
        var presetId = session.ViewModel.Styles.SelectedPreset?.Id;
        Guid? firstCueId;
        session.SetProjectBusy(true);
        try
        {
            var text = await ReadSubtitleFileAsync(path);
            var prepared = captured;
            ImmutableArray<SubtitleLine> lines;
            AssImportResult? assResult = null;
            if (ass)
            {
                var result = AssSubtitleFormat.Parse(text, captured.Width, captured.Height);
                if (!await ConfirmConversionAsync(result.Diagnostics))
                {
                    return;
                }
                lines = result.Lines;
                assResult = result;
            }
            else
            {
                lines = SubtitleTextFormat.ParseSrt(text);
                var creation = await session.Styles.PrepareCreationAsync(trackId, presetId);
                prepared = creation.Project;
                lines = lines.Select(line => line with { Style = creation.Style }).ToImmutableArray();
            }
            if (session.IsClosing || !ReferenceEquals(captured, session.Editor.Snapshot))
            {
                return;
            }
            var imported = assResult is null
                ? ProjectEditingOperations.ImportSubtitleLines(prepared, lines, Path.GetFileNameWithoutExtension(path))
                : ProjectEditingOperations.ImportSubtitleLines(prepared, assResult, Path.GetFileNameWithoutExtension(path));
            session.Editor.Apply("Import subtitles", _ => imported);
            firstCueId = lines.IsEmpty ? null : lines[0].Id;
            session.LogInfo("Subtitles", $"{Localization.Get("WorkflowLog.SubtitlesImported")} ({lines.Length})", path);
        }
        finally
        {
            session.SetProjectBusy(false);
        }
        if (firstCueId is { } id)
        {
            var line = session.Editor.Snapshot.Subtitles.First(value => value.Id == id);
            session.SelectTrack(line.TrackId);
            session.SelectCue(id);
        }
    }

    internal async Task ExportSubtitlesAsync(bool ass = false)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        session.SetProjectBusy(true);
        try
        {
            var document = session.Editor.Snapshot;
            var result = ass ? AssSubtitleFormat.Write(document) : new SubtitleFormatWriteResult(
                SubtitleTextFormat.WriteSrt(document.Subtitles.OrderBy(line => line.Start)),
                SubtitleFormatLossAnalysis.ForSrt(document));
            if (!await ConfirmConversionAsync(result.Diagnostics) || session.IsClosing)
            {
                return;
            }
            var extension = ass ? ".ass" : ".srt";
            var path = await dialogs.SaveFileAsync("ExportText", "SubtitleFiles", ["*" + extension], extension, "subtitles" + extension);
            if (path is null || session.IsClosing)
            {
                return;
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, result.Text, new UTF8Encoding(false, true));
                File.Move(temporary, path, true);
                session.LogInfo("Subtitles", Localization.Get("WorkflowLog.SubtitlesExported"), path);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    private Task<bool> ConfirmConversionAsync(ImmutableArray<SubtitleFormatDiagnostic> diagnostics)
    {
        if (diagnostics.IsEmpty)
        {
            return Task.FromResult(true);
        }
        var messages = diagnostics.Select(item => item.SubtitleId is { } id
            ? $"[{id}] {item.Code}: {item.Message}" : $"{item.Code}: {item.Message}").ToArray();
        return dialogs.ConfirmSubtitleConversionAsync(messages);
    }

    private static async Task<string> ReadSubtitleFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MAX_SUBTITLE_FILE_BYTES)
        {
            throw new InvalidDataException(Localization.Get("Workbench.SubtitleFileTooLarge"));
        }
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes);
        if (await stream.ReadAsync(new byte[1]) > 0)
        {
            throw new InvalidDataException(Localization.Get("Workbench.SubtitleFileTooLarge"));
        }
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes.AsSpan(offset));
    }

    internal bool IsPreviewBindingSynchronized()
    {
        var document = session.Editor.Snapshot;
        var path = document.Media is { } binding &&
            unavailableMediaBinding?.Matches(document, session.ProjectDirectory) != true
            ? ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId), session.ProjectDirectory)
            : null;
        return string.Equals(path, session.Controller.Snapshot.FilePath, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal async Task SynchronizePreviewBindingAsync()
    {
        session.SetProjectBusy(true);
        try
        {
            var document = session.Editor.Snapshot;
            if (document.Media is not { } binding ||
                unavailableMediaBinding?.Matches(document, session.ProjectDirectory) == true)
            {
                await session.Controller.CloseMediaAsync();
                await session.Analysis.ClearAsync();


            }
            else
            {
                var path = ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId),
                    session.ProjectDirectory);
                await session.Controller.OpenAsync(path);
                if (session.Controller.Snapshot.Error is { } error)
                {
                    throw error;
                }

                ProjectMediaBindingValidator.Validate(document,
                    session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。"));
                await session.Analysis.StartAsync(path);
            }

            session.Tick();
        }
        catch
        {
            await ClearPreviewBindingAsync();
            throw;
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task ClearPreviewBindingAsync()
    {
        await session.Controller.CloseMediaAsync();
        await session.Analysis.ClearAsync();



    }

}
