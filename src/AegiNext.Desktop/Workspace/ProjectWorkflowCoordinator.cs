using System.Collections.Immutable;
using System.Text;
using AegiNext.Application;
using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectWorkflowCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private const int MAX_SUBTITLE_FILE_BYTES = 16 * 1024 * 1024;
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
        if (session.IsProjectBusy || !await ConfirmDiscardOrSaveAsync() || session.IsClosing)
        {
            return;
        }

        session.SetProjectBusy(true);
        try
        {
            await session.Controller.CloseMediaAsync();
            await session.Analysis.ClearAsync();

            session.SetProjectLocation(null, session.ScratchDirectory);
            session.ResetSelection();
            session.Editor.Reset(new());
            session.LogInfo("Project", Localization.Get("WorkflowLog.ProjectCreated"), session.Editor.Snapshot.Name);
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

        var path = await dialogs.OpenFileAsync("OpenProject", "Projects", ["*.aeginext"]);
        if (path is null || !await ConfirmDiscardOrSaveAsync() || session.IsClosing)
        {
            return;
        }

        session.SetProjectBusy(true);
        var previousDocument = session.Editor.Snapshot;
        var previousDirectory = session.ProjectDirectory;
        var previousPosition = session.Controller.Snapshot.Position;
        var committed = false;
        try
        {
            var document = await ProjectStore.LoadAsync(path);
            var directory = Path.GetDirectoryName(path)!;
            var mediaPath = document.Media is { } binding
                ? ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId), directory)
                : null;
            if (mediaPath is null)
            {
                await session.Controller.CloseMediaAsync();
            }
            else
            {
                await session.Controller.OpenAsync(mediaPath);
                if (session.Controller.Snapshot.Error is { } error)
                {
                    throw error;
                }

                var media = session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。");
                ProjectMediaBindingValidator.Validate(document, media);
            }

            session.SetProjectLocation(path, directory);
            session.ResetSelection();
            session.Editor.Reset(document);
            committed = true;
            if (mediaPath is not null)
            {
                await session.Controller.SeekAsync(session.Controller.Snapshot.Start ?? MediaTime.Zero);
                await session.Analysis.StartAsync(mediaPath);
            }
            else
            {
                await session.Analysis.ClearAsync();

            }
            session.LogInfo("Project", Localization.Get("WorkflowLog.ProjectOpened"), path);
        }
        catch (Exception error)
        {
            if (!committed)
            {
                await RestorePreviewAsync(previousDocument, previousDirectory, previousPosition, error);
            }

            throw;
        }
        finally
        {
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
        var previousDirectory = session.ProjectDirectory;
        var previousPosition = session.Controller.Snapshot.Position;
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
                await RestorePreviewAsync(previousDocument, previousDirectory, previousPosition, error);
            }

            throw;
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task RestorePreviewAsync(ProjectDocument document, string directory, MediaTime position,
        Exception originalError)
    {
        try
        {
            if (document.Media is not { } binding)
            {
                await session.Controller.CloseMediaAsync();
                return;
            }

            var path = ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId),
                directory);
            await session.Controller.OpenAsync(path);
            if (session.Controller.Snapshot.Error is { } error)
            {
                throw error;
            }

            ProjectMediaBindingValidator.Validate(document,
                session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。"));
            await session.Controller.SeekAsync(position);
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
        if (session.IsProjectBusy || !session.TryCommitDrafts())
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

        session.SetProjectBusy(true);
        try
        {
            var snapshot = session.Editor.Snapshot;
            var directory = Path.GetDirectoryName(destination)!;
            var sameDirectory = directory == session.ProjectDirectory;
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

            session.LogInfo("Project", Localization.Get("Workbench.Saved"), destination);
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
            if (ass)
            {
                var result = AssSubtitleFormat.Parse(text, captured.Width, captured.Height);
                if (!await ConfirmConversionAsync(result.Diagnostics))
                {
                    return;
                }
                lines = result.Lines;
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
            var imported = ProjectEditingOperations.ImportSubtitleLines(prepared, lines, Path.GetFileNameWithoutExtension(path));
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

    internal async Task SynchronizePreviewBindingAsync()
    {
        session.SetProjectBusy(true);
        try
        {
            var document = session.Editor.Snapshot;
            if (document.Media is not { } binding)
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
