using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;

namespace AegiNext.Desktop.Workspace;

internal sealed class PlaybackSeekingCoordinator(WorkbenchSession session, VideoPreviewController controller)
{
    private long revision;
    internal MediaTime? PendingPosition { get; private set; }

    internal void Invalidate()
    {
        revision++;
        PendingPosition = null;
    }

    internal async Task SeekAsync(MediaTime position, bool clearEditingTarget = true)
    {
        session.InvalidateTimingSession();
        if (clearEditingTarget)
        {
            if (!session.TryCommitDrafts())
            {
                return;
            }
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
                PendingPosition = null;
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

    internal Task SeekProjectTimeAsync(MediaTime time) => SeekAsync((controller.Snapshot.Start ?? MediaTime.Zero) + time);
}
