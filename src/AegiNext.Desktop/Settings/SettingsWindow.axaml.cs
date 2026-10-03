using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace AegiNext.Desktop.Settings;

/// <summary>设置宿主，只组合页面、呈现主题并转发语义请求。</summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>供 XAML 加载器构造默认设置宿主。</summary>
    public SettingsWindow() : this(new WorkbenchPreferences())
    {
    }

    /// <summary>创建使用显式偏好快照的设置窗口。</summary>
    public SettingsWindow(WorkbenchPreferences preferences) : this(new SettingsWindowViewModel(preferences), preferences)
    {
    }

    /// <summary>由组合根注入设置模型，窗口不读取全局偏好。</summary>
    public SettingsWindow(SettingsWindowViewModel viewModel, WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        TitleBar = this.FindControl<WindowTitleBar>("SettingsTitleBar")!;
        viewModel.Appearance.Changed += (_, value) => AppearanceChanged?.Invoke(this, value);
        viewModel.Shortcuts.Changed += (_, value) => ShortcutsChanged?.Invoke(this, value);
        viewModel.Styles.UpsertRequested += (_, value) => UpsertStyleRequested?.Invoke(this, value);
        viewModel.Styles.DeleteRequested += (_, value) => DeleteStyleRequested?.Invoke(this, value);
        viewModel.Styles.ApplyRequested += (_, value) => ApplyStyleRequested?.Invoke(this, value);
        viewModel.Styles.CaptureRequested += (_, _) => CaptureStyleRequested?.Invoke(this, EventArgs.Empty);
        viewModel.Styles.ImportRequested += (_, _) => ImportStylesRequested?.Invoke(this, EventArgs.Empty);
        viewModel.Styles.ExportRequested += (_, _) => ExportStylesRequested?.Invoke(this, EventArgs.Empty);
        Deactivated += (_, _) => viewModel.Shortcuts.IsRecording = false;
        Closed += (_, _) => viewModel.Shortcuts.IsRecording = false;
        UpdatePreferences(preferences);
    }

    public event EventHandler<SettingsAppearanceChangedEventArgs>? AppearanceChanged;
    public event EventHandler<SettingsShortcutsChangedEventArgs>? ShortcutsChanged;
    public event EventHandler<SettingsStyleEventArgs>? UpsertStyleRequested;
    public event EventHandler<SettingsStyleDeleteEventArgs>? DeleteStyleRequested;
    public event EventHandler? CaptureStyleRequested;
    public event EventHandler<SettingsStyleEventArgs>? ApplyStyleRequested;
    public event EventHandler? ImportStylesRequested;
    public event EventHandler? ExportStylesRequested;
    public SettingsWindowViewModel ViewModel { get; }
    public WindowTitleBar TitleBar { get; }
    public SettingsPage CurrentPage => ViewModel.CurrentPage;
    public bool IsShortcutCaptureActive => ViewModel.Shortcuts.IsRecording;

    /// <summary>选择页面，所有草稿保持在页面模型中。</summary>
    public void SelectPage(SettingsPage page)
    {
        if (!Enum.IsDefined(page))
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        ViewModel.PageIndex = (int)page;
    }

    /// <summary>更新已经持久化的外观；快捷键编辑草稿保持独立。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        RequestedThemeVariant = value.Theme switch
        {
            WorkbenchTheme.LIGHT => ThemeVariant.Light,
            WorkbenchTheme.DARK => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        ViewModel.Appearance.UpdatePreferences(value);
        RefreshLanguage();
    }

    /// <summary>即时刷新三个页面的语言，保留未确认输入。</summary>
    public void RefreshLanguage()
    {
        SettingsViewLocalization.Apply(this);
        ViewModel.RefreshLanguage();
    }

    /// <summary>同步已持久化快捷键。</summary>
    public void UpdateShortcuts(IEnumerable<ShortcutBinding> bindings)
    {
        ViewModel.Shortcuts.UpdateBindings(bindings);
    }

    /// <summary>同步已持久化样式库和选择。</summary>
    public void UpdateStyles(IEnumerable<SubtitleStylePreset> presets, Guid? selectedId = null)
    {
        ViewModel.Styles.UpdateStyles(presets, selectedId);
    }

    /// <summary>工程流程执行期间禁止继续改变样式草稿。</summary>
    public void SetStyleOperationBusy(bool busy)
    {
        ViewModel.Styles.IsBusy = busy;
    }

    /// <summary>同步字幕选择允许的样式操作。</summary>
    public void UpdateSelectionAvailability(bool available)
    {
        ViewModel.Styles.HasSelectedSubtitle = available;
    }

    /// <summary>显示会话存储或工程流程错误。</summary>
    public void ShowError(string? message)
    {
        ViewModel.ShowError(message);
    }
}
