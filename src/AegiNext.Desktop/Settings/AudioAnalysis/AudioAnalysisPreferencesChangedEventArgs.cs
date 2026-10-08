namespace AegiNext.Desktop.Settings.AudioAnalysis;

/// <summary>携带已经确认的音频分析偏好。</summary>
public sealed class AudioAnalysisPreferencesChangedEventArgs(AudioAnalysisPreferences preferences) : EventArgs
{
    public AudioAnalysisPreferences Preferences { get; } = preferences;
}
