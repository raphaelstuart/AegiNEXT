using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Settings.Media;

public sealed partial class MediaSettingsViewModel
{
    private ImmutableArray<AudioDeviceCalibration> audioCalibrations = [];
    private AudioOutputClockSnapshot? audioClock;
    private int confirmedDelayMilliseconds;
    private string extraDelayMillisecondsText = "0";
    private bool invalidCalibration;

    public event EventHandler<SettingsAudioCalibrationChangedEventArgs>? AudioCalibrationChanged;
    public bool CanCalibrate => !IsBusy && audioClock is { Quality: not AudioClockQuality.UNAVAILABLE };
    public string AudioClockStatus => audioClock is null
        ? Localization.Get("Settings.AudioNoDevice")
        : audioClock.Quality == AudioClockQuality.UNAVAILABLE
            ? Localization.Get("Settings.AudioClockUnavailable")
            : Localization.Format(audioClock.Quality == AudioClockQuality.SYSTEM ? "Settings.AudioSystemClock" : "Settings.AudioEstimatedClock",
                audioClock.Backend, audioClock.SampleRate, audioClock.Channels);
    public string AudioCalibrationStatus => audioClock is null || !audioCalibrations.Any(value => value.Matches(audioClock))
        ? Localization.Get("Settings.AudioUncalibrated") : Localization.Get("Settings.AudioCalibrated");
    public string? AudioCalibrationError => invalidCalibration ? Localization.Get("Settings.AudioCalibrationInvalid") : null;
    public string ExtraDelayMillisecondsText
    {
        get => extraDelayMillisecondsText;
        set => SetProperty(ref extraDelayMillisecondsText, value);
    }

    /// <summary>同步实际音频时钟；设备或格式替换时恢复新设备自己的草稿。</summary>
    public void UpdateAudioStatus(AudioOutputClockSnapshot? clock)
    {
        var changed = audioClock?.DeviceId != clock?.DeviceId || audioClock?.Backend != clock?.Backend ||
            audioClock?.SampleRate != clock?.SampleRate || audioClock?.Channels != clock?.Channels;
        audioClock = clock;
        if (changed)
        {
            confirmedDelayMilliseconds = clock is null ? 0 : audioCalibrations.FirstOrDefault(value => value.Matches(clock))?.ExtraDelayMilliseconds ?? 0;
            RestoreAudioCalibration();
        }
        RefreshAudioLanguage();
    }

    /// <summary>确认整数毫秒校准；无效输入保留原文且不产生保存请求。</summary>
    public bool CommitAudioCalibration()
    {
        if (!CanCalibrate || audioClock is not { } clock)
        {
            return false;
        }
        if (!int.TryParse(ExtraDelayMillisecondsText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value is < -1000 or > 1000)
        {
            invalidCalibration = true;
            OnPropertyChanged(nameof(AudioCalibrationError));
            return false;
        }
        var profile = new AudioDeviceCalibration(clock.DeviceId, clock.Backend, clock.SampleRate, clock.Channels, value);
        var changed = !audioCalibrations.Contains(profile);
        confirmedDelayMilliseconds = value;
        RestoreAudioCalibration();
        if (changed)
        {
            AudioCalibrationChanged?.Invoke(this, new(profile));
        }
        return true;
    }

    /// <summary>恢复当前设备已确认的值，仅清除该字段的错误。</summary>
    public void RestoreAudioCalibration()
    {
        ExtraDelayMillisecondsText = confirmedDelayMilliseconds.ToString(CultureInfo.CurrentCulture);
        invalidCalibration = false;
        OnPropertyChanged(nameof(AudioCalibrationError));
    }

    private void UpdateCalibrationPreferences(WorkbenchPreferences preferences)
    {
        var untouched = ExtraDelayMillisecondsText == confirmedDelayMilliseconds.ToString(CultureInfo.CurrentCulture);
        audioCalibrations = preferences.AudioCalibrations;
        confirmedDelayMilliseconds = audioClock is null ? 0 : audioCalibrations.FirstOrDefault(value => value.Matches(audioClock))?.ExtraDelayMilliseconds ?? 0;
        if (untouched)
        {
            RestoreAudioCalibration();
        }
        RefreshAudioLanguage();
    }

    private void RefreshAudioLanguage()
    {
        OnPropertyChanged(nameof(CanCalibrate));
        OnPropertyChanged(nameof(AudioClockStatus));
        OnPropertyChanged(nameof(AudioCalibrationStatus));
        OnPropertyChanged(nameof(AudioCalibrationError));
    }
}
