using System.Globalization;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Tasks;

/// <summary>任务并行设置草稿，保留无效原文并仅提交有效上限。</summary>
public sealed class TaskSettingsViewModel : ObservableObject
{
    private int maximumConcurrentTasks;
    private string maximumConcurrentTasksText;
    private bool invalidMaximumConcurrentTasks;

    /// <summary>使用已加载偏好初始化任务设置。</summary>
    public TaskSettingsViewModel(WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        maximumConcurrentTasks = preferences.MaximumConcurrentTasks;
        maximumConcurrentTasksText = Format(maximumConcurrentTasks);
    }

    public event EventHandler<TaskSettingsChangedEventArgs>? Changed;
    public int MaximumConcurrentTasks => maximumConcurrentTasks;
    public string? Error => invalidMaximumConcurrentTasks ? Localization.Get("Settings.MaximumConcurrentTasksInvalid") : null;

    public string MaximumConcurrentTasksText
    {
        get => maximumConcurrentTasksText;
        set => SetProperty(ref maximumConcurrentTasksText, value);
    }

    /// <summary>确认 1 至 32 的整数；非法输入保留原文且不更新运行设置。</summary>
    public bool CommitMaximumConcurrentTasks()
    {
        if (!int.TryParse(MaximumConcurrentTasksText, NumberStyles.Integer, CultureInfo.CurrentCulture,
                out var maximum) || maximum is < 1 or > 32)
        {
            invalidMaximumConcurrentTasks = true;
            OnPropertyChanged(nameof(Error));
            return false;
        }

        var changed = maximum != maximumConcurrentTasks;
        maximumConcurrentTasks = maximum;
        OnPropertyChanged(nameof(MaximumConcurrentTasks));
        RestoreMaximumConcurrentTasks();
        if (changed)
        {
            Changed?.Invoke(this, new(maximum));
        }

        return true;
    }

    /// <summary>恢复本字段已经确认的并行上限。</summary>
    public void RestoreMaximumConcurrentTasks()
    {
        MaximumConcurrentTasksText = Format(maximumConcurrentTasks);
        invalidMaximumConcurrentTasks = false;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>同步持久化偏好，保留正在输入或无效的原始草稿。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        if (MaximumConcurrentTasksText == Format(maximumConcurrentTasks))
        {
            MaximumConcurrentTasksText = Format(value.MaximumConcurrentTasks);
        }

        maximumConcurrentTasks = value.MaximumConcurrentTasks;
        OnPropertyChanged(nameof(MaximumConcurrentTasks));
    }

    /// <summary>刷新验证错误语言，保持输入原文。</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Error));

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);
}
