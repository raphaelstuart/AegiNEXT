using AegiNext.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Appearance;

/// <summary>即时外观设置；保存和所有窗口的应用由工作台协调。</summary>
public sealed class AppearanceSettingsViewModel : ObservableObject
{
    private bool updating;
    private int themeIndex;
    private int languageIndex;
    private int menuLocationIndex;
    private string[] themes = [];
    private string[] languages = [];
    private string[] menuLocations = [];

    /// <summary>构造外观草稿，不读写全局设置。</summary>
    public AppearanceSettingsViewModel(WorkbenchPreferences preferences, bool? isMacOs = null)
    {
        ShowMenuLocation = isMacOs ?? OperatingSystem.IsMacOS();
        UpdatePreferences(preferences);
    }

    public event EventHandler<SettingsAppearanceChangedEventArgs>? Changed;
    public bool ShowMenuLocation { get; }
    public string[] Themes => themes;
    public string[] Languages => languages;
    public string[] MenuLocations => menuLocations;

    public int ThemeIndex
    {
        get => themeIndex;
        set
        {
            if (SetProperty(ref themeIndex, value))
            {
                NotifyChanged();
            }
        }
    }

    public int LanguageIndex
    {
        get => languageIndex;
        set
        {
            if (SetProperty(ref languageIndex, value))
            {
                NotifyChanged();
            }
        }
    }

    public int MenuLocationIndex
    {
        get => menuLocationIndex;
        set
        {
            if (SetProperty(ref menuLocationIndex, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>回填已应用的偏好，避免发出用户修改事件。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        updating = true;
        try
        {
            ThemeIndex = (int)value.Theme;
            LanguageIndex = value.Language switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };
            MenuLocationIndex = value.WindowMenuOnMac ? 1 : 0;
            RefreshLanguage();
        }
        finally
        {
            updating = false;
        }
    }

    /// <summary>刷新展示语言并保留输入值。</summary>
    public void RefreshLanguage()
    {
        var wasUpdating = updating;
        var previousTheme = themeIndex;
        var previousLanguage = languageIndex;
        var previousLocation = menuLocationIndex;
        updating = true;
        try
        {
            themes = [SettingsText.Get("System"), SettingsText.Get("Light"), SettingsText.Get("Dark")];
            languages = [SettingsText.Get("System"), "简体中文", "English"];
            menuLocations = [SettingsText.Get("SystemMenu"), SettingsText.Get("WindowMenu")];
            OnPropertyChanged(nameof(Themes));
            OnPropertyChanged(nameof(Languages));
            OnPropertyChanged(nameof(MenuLocations));
            themeIndex = previousTheme;
            languageIndex = previousLanguage;
            menuLocationIndex = previousLocation;
            OnPropertyChanged(nameof(ThemeIndex));
            OnPropertyChanged(nameof(LanguageIndex));
            OnPropertyChanged(nameof(MenuLocationIndex));
        }
        finally
        {
            updating = wasUpdating;
        }
    }

    private void NotifyChanged()
    {
        if (updating || ThemeIndex is < 0 or > 2 || LanguageIndex is < 0 or > 2 || MenuLocationIndex is < 0 or > 1)
        {
            return;
        }

        var language = LanguageIndex switch { 1 => "zh-CN", 2 => "en-US", _ => "system" };
        var preferences = new WorkbenchPreferences
        {
            Theme = (WorkbenchTheme)ThemeIndex, Language = language,
            WindowMenuOnMac = MenuLocationIndex == 1
        };
        preferences.Validate();
        Changed?.Invoke(this,
            new(preferences.Theme, preferences.Language, preferences.WindowMenuOnMac));
    }
}
