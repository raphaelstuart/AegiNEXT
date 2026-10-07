using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void SetInteractiveSeeking(bool value) => playback.SetInteractive(value);
    internal void CancelInteractiveSeeking() => playback.Invalidate();
    internal bool IsTransportPlaybackRequested => playback.IsPlaybackRequested;

    internal Task SeekForEditingAsync(MediaTime time) => playback.SeekForEditingAsync((controller.Snapshot.Start ?? MediaTime.Zero) + time);

    internal Task SeekFromUserAsync(MediaTime position) => playback.SeekFromUserAsync(position);
    internal Task SeekRelativeAsync(long seconds) => playback.SeekRelativeAsync(seconds);
    internal Task SeekProjectTimeAsync(MediaTime relative) => playback.SeekProjectTimeAsync(relative);

    private void OnPreviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (updatingWorkbench)
        {
            return;
        }

        if (e.PropertyName == "IsScrubbing")
        {
            SetInteractiveSeeking(ViewModel.Preview.IsScrubbing);
        }
        else if (e.PropertyName == "Volume")
        {
            var value = (float)ViewModel.Preview.Volume;
            controller.SetVolume(value);
            UpdatePreferences(current => current with { Volume = value });
        }
        else if (e.PropertyName == "IsMuted")
        {
            controller.SetMuted(ViewModel.Preview.IsMuted);
            ViewModel.Preview.MuteLabel = Localization.Get("Preview." + (ViewModel.Preview.IsMuted ? "Unmute" : "Mute"));
        }
        else if (e.PropertyName == "SelectedQuality" && ViewModel.Preview.SelectedQuality is { } quality &&
                 quality.Id != Preferences.PreviewQuality)
        {
            UpdatePreferences(current => current with { PreviewQuality = quality.Id });
        }
    }
    private void OnExportPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Crf")
        {
            ViewModel.Export.CrfText = ViewModel.Export.Crf?.ToString(InterfaceCulture) ?? string.Empty;
        }
        else if (e.PropertyName == "AudioBitrate")
        {
            ViewModel.Export.AudioBitrateText = ViewModel.Export.AudioBitrate?.ToString(InterfaceCulture) ?? string.Empty;
        }
        else if (e.PropertyName == "VideoBitrate")
        {
            ViewModel.Export.VideoBitrateText = ViewModel.Export.VideoBitrate?.ToString(InterfaceCulture) ?? string.Empty;
        }
    }
}
