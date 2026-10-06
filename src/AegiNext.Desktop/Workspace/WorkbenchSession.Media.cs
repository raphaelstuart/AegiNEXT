using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private bool switchingPreviewDecodeMode;
    private VideoDecodeSessionInfo? lastPreviewDecodeSessionInfo;

    internal event EventHandler? PreviewDecodeModeChanged;
    internal bool IsPreviewDecodeModeSwitching => switchingPreviewDecodeMode;
    internal VideoDecodeSessionInfo? PreviewDecodeSessionInfo => controller.Snapshot.DecodeSessionInfo;

    private void RefreshPreviewDecodeSessionInfo(VideoDecodeSessionInfo? info)
    {
        var previous = lastPreviewDecodeSessionInfo;
        lastPreviewDecodeSessionInfo = info;
        var changed = previous is null != (info is null) || previous is not null && info is not null &&
            (previous.RequestedMode != info.RequestedMode || previous.ActiveBackend != info.ActiveBackend ||
             previous.HardwareConfirmed != info.HardwareConfirmed || previous.FallbackReason != info.FallbackReason);
        if (!changed)
        {
            return;
        }

        if (info is not null)
        {
            var message = $"{info.RequestedMode}: {info.ActiveBackend}, hardware={info.HardwareConfirmed}.";
            if (info.FallbackReason is { Length: > 0 } reason)
            {
                LogWarning("Video decoder", message, reason);
            }
            else
            {
                LogInfo("Video decoder", message);
            }
        }

        PreviewDecodeModeChanged?.Invoke(this, EventArgs.Empty);
    }

    internal async Task<bool> SetPreviewDecodeModeAsync(VideoDecodeMode mode, CancellationToken cancellationToken = default)
    {
        if (closing || switchingPreviewDecodeMode || projectBusy)
        {
            return false;
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (mode == Preferences.PreviewDecodeMode)
        {
            return true;
        }

        switchingPreviewDecodeMode = true;
        playback.Invalidate();
        ViewModel.CancelGestures();
        Tick();
        PreviewDecodeModeChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            LastError = null;
            ViewModel.Error = null;
            await controller.SwitchDecodeModeAsync(mode, cancellationToken);
            if (closing)
            {
                return false;
            }

            UpdatePreferences(current => current with { PreviewDecodeMode = mode });
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception error)
        {
            if (!closing)
            {
                ShowError(error);
            }

            return false;
        }
        finally
        {
            switchingPreviewDecodeMode = false;
            if (!closing)
            {
                Tick();
                PreviewDecodeModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
