namespace AegiNext.Desktop.Settings.Preview;

/// <summary>携带已确认的试听时长，不包含原始输入草稿。</summary>
public sealed class PreviewSettingsChangedEventArgs(int subtitleAuditionMilliseconds) : EventArgs
{
    public int SubtitleAuditionMilliseconds { get; } = subtitleAuditionMilliseconds;
}
