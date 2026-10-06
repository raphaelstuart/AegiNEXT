using AegiNext.Desktop.Settings.Media;

namespace AegiNext.Desktop.Settings;

/// <summary>为当前输出设备提交的个人延迟校准。</summary>
public sealed class SettingsAudioCalibrationChangedEventArgs(AudioDeviceCalibration calibration) : EventArgs
{
    public AudioDeviceCalibration Calibration { get; } = calibration;
}
