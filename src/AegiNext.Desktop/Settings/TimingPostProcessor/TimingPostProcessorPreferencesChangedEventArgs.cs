namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>携带已确认的处理参数，不包含未完成的数值草稿。</summary>
public sealed class TimingPostProcessorPreferencesChangedEventArgs(TimingPostProcessorPreferences preferences) : EventArgs
{
    public TimingPostProcessorPreferences Preferences { get; } = preferences;
}
