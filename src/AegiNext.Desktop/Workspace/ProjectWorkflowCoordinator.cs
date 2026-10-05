using System.Collections.Immutable;
using System.Text;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Workspace.Diagnostics;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectWorkflowCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
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
            session.LogInfo("Project", WorkflowLogText.Get("ProjectCreated", session.InterfaceCulture), session.Editor.Snapshot.Name);
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
            session.LogInfo("Project", WorkflowLogText.Get("ProjectOpened", session.InterfaceCulture), path);
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
            session.LogInfo("Media", WorkflowLogText.Get("MediaOpened", session.InterfaceCulture), path);
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
            var prepared = directory == session.ProjectDirectory
                ? snapshot
                : await ProjectResources.RebaseAsync(snapshot, session.ProjectDirectory, directory);
            await ProjectStore.SaveAsync(prepared, destination);
            session.SetProjectLocation(destination, directory);
            if (!ReferenceEquals(prepared, snapshot))
            {
                session.Editor.Reset(prepared);
            }

            session.Editor.MarkSaved(prepared);
            session.LogInfo("Project", WorkbenchText.Get("Saved"), destination);
            return true;
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task ImportSubtitlesAsync()
    {
        if (session.IsProjectBusy)
        {
            return;
        }

        var path = await dialogs.OpenFileAsync("Import", "SubtitleFiles", ["*.srt", "*.txt"]);
        if (path is null)
        {
            return;
        }

        var trackId = session.CurrentTrackId;
        var presetId = session.ViewModel.Styles.SelectedPreset?.Id;
        var importStart = session.ProjectPosition;
        Guid? firstCueId = null;
        session.SetProjectBusy(true);
        try
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("字幕文件超过 16 MiB。");
            }

            var text = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true));
            var lines = Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase)
                ? SubtitleTextFormat.ParseSrt(text)
                : SubtitleTextFormat.ImportText(text, start: importStart);
            await session.CreateSubtitleClipsAsync(lines, trackId, presetId);
            firstCueId = lines.IsEmpty ? null : lines[0].Id;
            session.LogInfo("Subtitles", $"{WorkflowLogText.Get("SubtitlesImported", session.InterfaceCulture)} ({lines.Length})", path);
        }
        finally
        {
            session.SetProjectBusy(false);
        }

        if (firstCueId is { } id)
        {
            session.SelectCue(id);
        }
    }

    internal async Task ExportSubtitlesAsync()
    {
        if (!session.TryCommitDrafts())
        {
            return;
        }

        var text = SubtitleTextFormat.WriteSrt(session.Editor.Snapshot.Subtitles);
        var path = await dialogs.SaveFileAsync("ExportText", "SubtitleFiles", ["*.srt"], ".srt", "subtitles.srt");
        if (path is null)
        {
            return;
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, true);
            session.LogInfo("Subtitles", WorkflowLogText.Get("SubtitlesExported", session.InterfaceCulture), path);
        }
        finally
        {
            File.Delete(temporary);
        }
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
