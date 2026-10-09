using AegiNext.Application;
using AegiNext.Application.Tasks;
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
        var captured = session.Editor.Snapshot;
        var directory = session.ProjectDirectory;
        var inputRevision = session.TaskInputRevision;
        IReadOnlyList<string> paths;
        using (selectedPaths is null ? session.AcquireEditingLease() : null)
        {
            paths = selectedPaths ?? await dialogs.OpenFilesAsync("MergeProjects", "Projects", ["*.aeginext"])
                .WaitAsync(token);
        }
        token.ThrowIfCancellationRequested();
        if (paths.Count == 0 || session.IsClosing)
        {
            return;
        }
        EnsureMergeTargetUnchanged(captured, directory, inputRevision);
        var merged = await session.ApplicationContext.Tasks.Submit(new MergeProjectsTask(session, this,
            captured, directory, paths.ToArray(), inputRevision)).WaitAsync(cancellationToken);

        if (session.IsClosing)
        {
            return;
        }
        if (!merged.ImportedSubtitleIds.IsEmpty)
        {
            var id = merged.ImportedSubtitleIds[0];
            session.SelectTrack(session.ClipIndex.GetSubtitleTrackId(id));
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

    internal async Task<ProjectMergeResult> MergeProjectsCoreAsync(ProjectDocument captured, string directory,
        IReadOnlyList<string> paths, long inputRevision, AegiTaskExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
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

        EnsureMergeTargetUnchanged(captured, directory, inputRevision);
        AegiTaskEditLease? editLease = null;
        try
        {
            await using var resources = await ProjectMergeResources.PrepareAsync(sources, directory, cancellationToken);
            var result = ProjectEditingOperations.MergeProjects(captured, resources.Sources);
            ProjectStore.ValidateSerialization(result.Document);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureMergeTargetUnchanged(captured, directory, inputRevision);
            editLease = context.AcquireEditLease();
            context.EnterCommit(() => !session.IsClosing && inputRevision == session.TaskInputRevision &&
                !session.HasProjectDrafts && ReferenceEquals(captured, session.Editor.Snapshot) && directory == session.ProjectDirectory);
            await resources.CommitAsync(CancellationToken.None);
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
        finally
        {
            editLease?.Dispose();
        }
    }

    private void EnsureMergeTargetUnchanged(ProjectDocument captured, string directory, long inputRevision)
    {
        if (session.IsClosing)
        {
            throw new OperationCanceledException(session.ProjectOperationsToken);
        }
        if (session.TaskInputRevision != inputRevision || session.HasProjectDrafts ||
            !ReferenceEquals(captured, session.Editor.Snapshot) || directory != session.ProjectDirectory)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.ProjectMergeChanged"));
        }
    }
}
