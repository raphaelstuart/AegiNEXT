using System.Globalization;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Preview;

/// <summary>预览设置草稿；输入确认与共享偏好的持久化保持独立。</summary>
public sealed class PreviewSettingsViewModel : ObservableObject
{
    private int subtitleAuditionMilliseconds;
    private string subtitleAuditionMillisecondsText;
    private bool invalidAuditionMilliseconds;

    /// <summary>使用已加载的偏好初始化试听时长及其原始输入。</summary>
    public PreviewSettingsViewModel(WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        subtitleAuditionMilliseconds = preferences.SubtitleAuditionMilliseconds;
        subtitleAuditionMillisecondsText = Format(subtitleAuditionMilliseconds);
    }

    public event EventHandler<PreviewSettingsChangedEventArgs>? Changed;
    public int SubtitleAuditionMilliseconds => subtitleAuditionMilliseconds;
    public string? Error => invalidAuditionMilliseconds ? Localization.Get("Settings.SubtitleAuditionMillisecondsInvalid") : null;

    public string SubtitleAuditionMillisecondsText
    {
        get => subtitleAuditionMillisecondsText;
        set => SetProperty(ref subtitleAuditionMillisecondsText, value);
    }

    /// <summary>提交正整数毫秒；无效输入保持原文且不更新偏好。</summary>
    public bool CommitSubtitleAuditionMilliseconds()
    {
        if (!int.TryParse(SubtitleAuditionMillisecondsText, NumberStyles.Integer, CultureInfo.CurrentCulture,
                out var milliseconds) || milliseconds < 1)
        {
            invalidAuditionMilliseconds = true;
            OnPropertyChanged(nameof(Error));
            return false;
        }

        var changed = milliseconds != subtitleAuditionMilliseconds;
        subtitleAuditionMilliseconds = milliseconds;
        OnPropertyChanged(nameof(SubtitleAuditionMilliseconds));
        RestoreSubtitleAuditionMilliseconds();
        if (changed)
        {
            Changed?.Invoke(this, new(milliseconds));
        }

        return true;
    }

    /// <summary>恢复已确认的试听时长，并清除本字段的验证错误。</summary>
    public void RestoreSubtitleAuditionMilliseconds()
    {
        SubtitleAuditionMillisecondsText = Format(subtitleAuditionMilliseconds);
        invalidAuditionMilliseconds = false;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>回填已保存偏好，保留尚未确认的原始输入。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        if (SubtitleAuditionMillisecondsText == Format(subtitleAuditionMilliseconds))
        {
            SubtitleAuditionMillisecondsText = Format(value.SubtitleAuditionMilliseconds);
        }

        subtitleAuditionMilliseconds = value.SubtitleAuditionMilliseconds;
        OnPropertyChanged(nameof(SubtitleAuditionMilliseconds));
    }

    /// <summary>刷新错误展示语言并保留未确认输入。</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Error));

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);
}
