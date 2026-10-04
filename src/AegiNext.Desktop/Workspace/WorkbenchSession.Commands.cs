using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool CanExecuteCommand(WorkbenchCommand command)
    {
        if (closing || projectBusy && command != WorkbenchCommand.VIEW_LOG)
        {
            return false;
        }

        return command switch
        {
            WorkbenchCommand.UNDO => editor.CanUndo,
            WorkbenchCommand.REDO => editor.CanRedo,
            WorkbenchCommand.PLAY_PAUSE or WorkbenchCommand.SEEK_BACKWARD or WorkbenchCommand.SEEK_FORWARD =>
                controller.Snapshot.Error is null && controller.Snapshot.State is VideoPlaybackState.PAUSED or VideoPlaybackState.PLAYING or VideoPlaybackState.ENDED,
            WorkbenchCommand.TIMING_ENTER => playback.PendingPosition is null && controller.Snapshot.Error is null && controller.Snapshot.State is VideoPlaybackState.PAUSED or VideoPlaybackState.PLAYING or VideoPlaybackState.ENDED,
            WorkbenchCommand.TIMING_EXIT => timingSession.ActiveCueId is not null,
            WorkbenchCommand.DELETE_SUBTITLE or WorkbenchCommand.SPLIT_SUBTITLE or WorkbenchCommand.MERGE_SUBTITLE => SelectedCue is not null,
            WorkbenchCommand.EXPORT_VIDEO => editor.Snapshot.Media is not null && export.CanStart,
            _ => true
        };
    }

    internal async Task ExecuteCommandAsync(WorkbenchCommand command)
    {
        if (!CanExecuteCommand(command))
        {
            return;
        }

        if (command is not (WorkbenchCommand.TIMING_ENTER or WorkbenchCommand.TIMING_EXIT))
        {
            InvalidateTimingSession();
        }

        await RunCommandAsync(async () =>
        {
            LogInfo("Command", command.ToString());
            switch (command)
            {
                case WorkbenchCommand.NEW_PROJECT: await workflow.NewProjectAsync(); break;
                case WorkbenchCommand.OPEN_PROJECT: await workflow.OpenProjectAsync(); break;
                case WorkbenchCommand.SAVE_PROJECT: await workflow.SaveProjectAsync(false); break;
                case WorkbenchCommand.SAVE_PROJECT_AS: await workflow.SaveProjectAsync(true); break;
                case WorkbenchCommand.OPEN_MEDIA:
                    var path = await dialogs.OpenFileAsync("Open", "Videos", ["*.mkv", "*.mp4", "*.mov", "*.webm", "*.avi", "*.m4v", "*.ts", "*.m2ts"]);
                    if (path is not null && !closing)
                    {
                        await workflow.OpenMediaAsync(path, true);
                    }
                    break;
                case WorkbenchCommand.IMPORT_SUBTITLES: await workflow.ImportSubtitlesAsync(); break;
                case WorkbenchCommand.EXPORT_SUBTITLES: await workflow.ExportSubtitlesAsync(); break;
                case WorkbenchCommand.EXPORT_VIDEO: await export.EncodeAsync(); break;
                case WorkbenchCommand.PLAY_PAUSE:
                    if (!TryCommitDrafts())
                    {
                        break;
                    }
                    ViewModel.CancelGestures();
                    ClearKeyframeSelection();
                    if (controller.Snapshot.State == VideoPlaybackState.PLAYING)
                    {
                        await controller.PauseAsync();
                    }
                    else
                    {
                        if (controller.Snapshot.State == VideoPlaybackState.ENDED)
                        {
                            await SeekFromUserAsync(controller.Snapshot.Start ?? MediaTime.Zero);
                        }
                        await controller.PlayAsync();
                    }
                    break;
                case WorkbenchCommand.SEEK_BACKWARD: await SeekRelativeAsync(-5); break;
                case WorkbenchCommand.SEEK_FORWARD: await SeekRelativeAsync(5); break;
                case WorkbenchCommand.UNDO:
                    if (TryCommitDrafts())
                    {
                        ViewModel.CancelGestures();
                        ResetTiming();
                        editor.Undo();
                    }
                    break;
                case WorkbenchCommand.REDO:
                    if (TryCommitDrafts())
                    {
                        ViewModel.CancelGestures();
                        ResetTiming();
                        editor.Redo();
                    }
                    break;
                case WorkbenchCommand.TIMING_ENTER:
                    if (TryCommitDrafts())
                    {
                        SetCueStart();
                    }
                    break;
                case WorkbenchCommand.TIMING_EXIT:
                    if (TryCommitDrafts())
                    {
                        SetCueEnd();
                    }
                    break;
                case WorkbenchCommand.ADD_SUBTITLE:
                    if (TryCommitDrafts())
                    {
                        AddCue();
                    }
                    break;
                case WorkbenchCommand.DELETE_SUBTITLE:
                    if (TryCommitDrafts() && SelectedCue is { } cue)
                    {
                        editor.RemoveSubtitle(cue.Id);
                        ResetTiming();
                    }
                    break;
                case WorkbenchCommand.SPLIT_SUBTITLE:
                    if (TryCommitDrafts())
                    {
                        SplitCue();
                        ResetTiming();
                    }
                    break;
                case WorkbenchCommand.MERGE_SUBTITLE:
                    if (TryCommitDrafts())
                    {
                        MergeCue();
                        ResetTiming();
                    }
                    break;
                default:
                    await ViewModel.RequestHostCommandAsync(command);
                    break;
            }
        });
    }

    internal Task CancelExportAsync()
    {
        export.Cancel();
        return Task.CompletedTask;
    }

    internal void ResetTiming()
    {
        timingSession = timingSession.Reset();
        ViewModel.RefreshCommands();
    }

    internal void InvalidateTimingSession()
    {
        if (timingSession.ActiveCueId is not null)
        {
            ResetTiming();
        }
    }
}
