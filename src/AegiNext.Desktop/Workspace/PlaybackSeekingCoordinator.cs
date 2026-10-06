using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed class PlaybackSeekingCoordinator(WorkbenchSession session, VideoPreviewController controller)
{
    private long revision;
    private readonly DispatcherTimer interactiveTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private MediaTime? queuedTarget;
    private bool seeking;
    internal bool IsInteractive { get; private set; }

    internal void SetInteractive(bool value)
    {
        if (IsInteractive == value)
        {
            return;
        }
        IsInteractive = value;
        if (value)
        {
            session.ViewModel.Timeline.ResumePlaybackFollow();
            session.TryCommitDrafts(false);
            session.CancelSceneGesture();
            session.ClearKeyframeSelection();
            _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
            interactiveTimer.Tick += OnInteractiveTick;
            interactiveTimer.Start();
        }
        else
        {
            interactiveTimer.Stop();
            interactiveTimer.Tick -= OnInteractiveTick;
            if ((queuedTarget ?? PendingPosition) is { } finalTarget)
            {
                queuedTarget = null;
                _ = session.RunCommandAsync(() => SeekAsync(finalTarget, false));
            }
        }
        session.Tick();
    }

    private async void OnInteractiveTick(object? sender, EventArgs e)
    {
        if (seeking || queuedTarget is not { } target)
        {
            return;
        }
        queuedTarget = null;
        seeking = true;
        try
        {
            await SeekAsync(target, false);
        }
        finally
        {
            seeking = false;
        }
    }
    internal MediaTime? PendingPosition { get; private set; }

    internal void Invalidate()
    {
        revision++;
        PendingPosition = null;
        queuedTarget = null;
        interactiveTimer.Stop();
        interactiveTimer.Tick -= OnInteractiveTick;
        IsInteractive = false;
    }

    internal async Task SeekAsync(MediaTime position, bool clearEditingTarget = true)
    {
        session.InvalidateTimingSession();
        if (clearEditingTarget)
        {
            session.ViewModel.Timeline.ResumePlaybackFollow();
            session.TryCommitDrafts(false);
            session.CancelSceneGesture();
            session.ClearKeyframeSelection();
        }
        var snapshot = controller.Snapshot;
        var start = snapshot.Start ?? MediaTime.Zero;
        var target = position < start ? start : position;
        if (snapshot.Duration is { } duration && target > start + duration)
        {
            target = start + duration;
        }

        var requestRevision = ++revision;
        PendingPosition = target;
        session.ViewModel.Error = null;
        session.Tick();
        try
        {
            await controller.SeekAsync(target);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!session.IsClosing && requestRevision == revision)
            {
                session.ShowError(error);
            }
        }
        finally
        {
            if (requestRevision == revision)
            {
                PendingPosition = queuedTarget;
                if (!session.IsClosing)
                {
                    session.Tick();
                }
            }
        }
    }

    internal Task SeekRelativeAsync(long seconds)
    {
        var snapshot = controller.Snapshot;
        var start = snapshot.Start ?? MediaTime.Zero;
        var target = (PendingPosition ?? snapshot.Position) + new MediaTime(seconds);
        return SeekAsync(target < start ? start : target);
    }

    internal Task SeekProjectTimeAsync(MediaTime time)
    {
        session.ViewModel.Timeline.ResumePlaybackFollow();
        var target = (controller.Snapshot.Start ?? MediaTime.Zero) + time;
        if (!IsInteractive)
        {
            return SeekAsync(target);
        }
        queuedTarget = PendingPosition = target;
        session.Tick();
        return Task.CompletedTask;
    }
}
