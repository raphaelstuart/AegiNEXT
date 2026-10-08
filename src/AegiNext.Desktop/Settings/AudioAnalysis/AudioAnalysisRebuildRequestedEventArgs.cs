namespace AegiNext.Desktop.Settings.AudioAnalysis;

/// <summary>携带显式应用的分析方案，允许重新生成相同方案。</summary>
public sealed class AudioAnalysisRebuildRequestedEventArgs(AudioAnalysisPreferences preferences) : EventArgs
{
    public AudioAnalysisPreferences Preferences { get; } = preferences;
}
