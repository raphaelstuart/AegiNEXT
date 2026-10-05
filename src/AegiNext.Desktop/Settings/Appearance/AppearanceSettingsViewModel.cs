using System.Collections.Immutable;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Appearance;

/// <summary>即时外观设置；保存和所有窗口的应用由工作台协调。</summary>
public sealed class AppearanceSettingsViewModel : ObservableObject
{
    private bool updating;
    private int themeIndex;
    private string languageId = "system";
    private int menuLocationIndex;
    private string[] themes = [];
    private ImmutableArray<LanguageInfo> languages = [];
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
    public IReadOnlyList<LanguageInfo> Languages => languages;
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

    public LanguageInfo? SelectedLanguage
    {
        get => languages.FirstOrDefault(language =>
            string.Equals(language.LanguageID, languageId, StringComparison.OrdinalIgnoreCase));
        set
        {
            var language = value is null ? null : languages.FirstOrDefault(language =>
                string.Equals(language.LanguageID, value.LanguageID, StringComparison.OrdinalIgnoreCase));
            if (language is null || string.Equals(languageId, language.LanguageID, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            languageId = language.LanguageID;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageIndex));
            NotifyChanged();
        }
    }

    public int LanguageIndex
    {
        get => SelectedLanguage is { } selected ? languages.IndexOf(selected) : -1;
        set
        {
            if (value >= 0 && value < languages.Length)
            {
                SelectedLanguage = languages[value];
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
            themeIndex = (int)value.Theme;
            languageId = value.Language;
            menuLocationIndex = value.WindowMenuOnMac ? 1 : 0;
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
        var previousLanguage = languageId;
        var previousLocation = menuLocationIndex;
        updating = true;
        try
        {
            themes = [Localization.Get("Settings.System"), Localization.Get("Settings.Light"), Localization.Get("Settings.Dark")];
            languages = new[] { new LanguageInfo(Localization.Get("Settings.System"), "system") }
                .Concat(Localization.KnownLanguages).ToImmutableArray();
            menuLocations = [Localization.Get("Settings.SystemMenu"), Localization.Get("Settings.WindowMenu")];
            OnPropertyChanged(nameof(Themes));
            OnPropertyChanged(nameof(Languages));
            OnPropertyChanged(nameof(MenuLocations));
            themeIndex = previousTheme;
            languageId = previousLanguage;
            menuLocationIndex = previousLocation;
            OnPropertyChanged(nameof(ThemeIndex));
            OnPropertyChanged(nameof(LanguageIndex));
            OnPropertyChanged(nameof(SelectedLanguage));
            OnPropertyChanged(nameof(MenuLocationIndex));
        }
        finally
        {
            updating = wasUpdating;
        }
    }

    private void NotifyChanged()
    {
        if (updating || ThemeIndex is < 0 or > 2 || MenuLocationIndex is < 0 or > 1)
        {
            return;
        }

        var preferences = new WorkbenchPreferences
        {
            Theme = (WorkbenchTheme)ThemeIndex, Language = languageId,
            WindowMenuOnMac = MenuLocationIndex == 1
        };
        preferences.Validate();
        Changed?.Invoke(this,
            new(preferences.Theme, preferences.Language, preferences.WindowMenuOnMac));
    }
}
