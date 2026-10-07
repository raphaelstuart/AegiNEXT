using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class ProjectWorkflowCoordinator
{
    internal async Task MergeProjectsAsync(IReadOnlyList<string>? selectedPaths = null,
        CancellationToken cancellationToken = default)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested ||
            !session.TryCommitDrafts())
        {
            return;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.ProjectOperationsToken);
        var token = lifetime.Token;
        ProjectMergeResult? merged = null;
        session.SetProjectBusy(true);
        try
        {
            var captured = session.Editor.Snapshot;
            var directory = session.ProjectDirectory;
            await using var pause = await session.Persistence.PauseAsync();
            var paths = selectedPaths ?? await dialogs.OpenFilesAsync("MergeProjects", "Projects", ["*.aeginext"])
                .WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (paths.Count == 0 || session.IsClosing)
            {
                return;
            }
            EnsureMergeTargetUnchanged(captured, directory);
            merged = await session.Persistence.RunExclusiveAsync(() => MergeProjectsCoreAsync(
                captured, directory, paths, token));
        }
        finally
        {
            session.SetProjectBusy(false);
        }

        if (merged is null || session.IsClosing)
        {
            return;
        }
        if (!merged.ImportedSubtitleIds.IsEmpty)
        {
            var id = merged.ImportedSubtitleIds[0];
            var line = session.Editor.Snapshot.Subtitles.First(value => value.Id == id);
            session.SelectTrack(line.TrackId);
            session.SelectCue(id);
        }
        else
        {
            if (!merged.ImportedTrackIds.IsEmpty)
            {
                session.SelectTrack(merged.ImportedTrackIds[0]);
            }
            if (!merged.ImportedLayerIds.IsEmpty)
            {
                var id = merged.ImportedLayerIds[0];
                session.SelectLayer(id, [id]);
            }
        }
    }

    private async Task<ProjectMergeResult> MergeProjectsCoreAsync(ProjectDocument captured, string directory,
        IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var sources = new List<ProjectMergeSource>(paths.Count);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(pathComparer);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            if (!seen.Add(fullPath))
            {
                throw new InvalidDataException(Localization.Format("Workbench.ProjectMergeDuplicateSource", fullPath));
            }
            if (session.ProjectPath is { } currentPath && pathComparer.Equals(fullPath, Path.GetFullPath(currentPath)))
            {
                throw new InvalidDataException(Localization.Get("Workbench.ProjectMergeCurrentSource"));
            }
            if (ProjectBackupStore.IsBackupPath(fullPath))
            {
                throw new InvalidDataException(Localization.Get("Workbench.BackupRestoreRequired"));
            }

            ProjectDocument source;
            try
            {
                source = await ProjectStore.LoadAsync(fullPath, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidDataException(Localization.Format("Workbench.ProjectMergeSourceFailed", fullPath, error.Message), error);
            }
            if (source.Width != captured.Width || source.Height != captured.Height)
            {
                throw new InvalidDataException(Localization.Format("Workbench.ProjectMergeCanvasMismatch", fullPath,
                    source.Width, source.Height, captured.Width, captured.Height));
            }
            if (source.ReferenceWhiteNits != captured.ReferenceWhiteNits)
            {
                throw new InvalidDataException(Localization.Format("Workbench.ProjectMergeWhiteMismatch", fullPath,
                    source.ReferenceWhiteNits, captured.ReferenceWhiteNits));
            }
            sources.Add(new(source, string.IsNullOrWhiteSpace(source.Name)
                ? Path.GetFileNameWithoutExtension(fullPath) : source.Name, Path.GetDirectoryName(fullPath)!));
        }

        EnsureMergeTargetUnchanged(captured, directory);
        await using var resources = await ProjectMergeResources.PrepareAsync(sources, directory, cancellationToken);
        var result = ProjectEditingOperations.MergeProjects(captured, resources.Sources);
        _ = ProjectStore.Serialize(result.Document);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMergeTargetUnchanged(captured, directory);
        await resources.CommitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureMergeTargetUnchanged(captured, directory);
        try
        {
            session.Editor.Apply("Merge projects", _ => result.Document);
        }
        finally
        {
            if (ReferenceEquals(session.Editor.Snapshot, result.Document))
            {
                resources.Accept();
            }
        }
        session.LogInfo("Project", Localization.Format("WorkflowLog.ProjectsMerged", sources.Count,
            result.ImportedTrackIds.Length, result.ImportedSubtitleIds.Length));
        return result;
    }

    private void EnsureMergeTargetUnchanged(ProjectDocument captured, string directory)
    {
        if (session.IsClosing)
        {
            throw new OperationCanceledException(session.ProjectOperationsToken);
        }
        if (!ReferenceEquals(captured, session.Editor.Snapshot) || directory != session.ProjectDirectory)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.ProjectMergeChanged"));
        }
    }
}
