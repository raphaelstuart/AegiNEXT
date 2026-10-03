using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal Task SeekFromUserAsync(MediaTime position) => playback.SeekAsync(position);
    internal Task SeekRelativeAsync(long seconds) => playback.SeekRelativeAsync(seconds);
    internal Task SeekProjectTimeAsync(MediaTime relative) => playback.SeekProjectTimeAsync(relative);

    private void OnPreviewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (updatingWorkbench)
        {
            return;
        }

        if (e.PropertyName == "Volume")
        {
            var value = (float)ViewModel.Preview.Volume;
            controller.SetVolume(value);
            preferences = preferences with { Volume = value };
            QueuePreferencesWrite();
        }
        else if (e.PropertyName == "IsMuted")
        {
            controller.SetMuted(ViewModel.Preview.IsMuted);
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
    }
}
