using System.ComponentModel;
using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Settings.Appearance;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Settings.Styles;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings;

/// <summary>设置导航和三个独立页面的组合模型，不拥有工程或控件。</summary>
public sealed class SettingsWindowViewModel : ObservableObject
{
    private int pageIndex;
    private string? externalError;
    private string title = SettingsText.Get("Settings");

    /// <summary>使用已加载偏好构造页面模型。</summary>
    public SettingsWindowViewModel(WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        Appearance = new(preferences);
        Shortcuts = new(preferences.ShortcutBindings);
        Styles = new();
        Shortcuts.PropertyChanged += PageModelChanged;
        Styles.PropertyChanged += PageModelChanged;
    }

    public AppearanceSettingsViewModel Appearance { get; }
    public ShortcutSettingsViewModel Shortcuts { get; }
    public StyleSettingsViewModel Styles { get; }
    public string Title => title;
    public SettingsPage CurrentPage => (SettingsPage)PageIndex;
    public bool IsAppearanceVisible => CurrentPage == SettingsPage.APPEARANCE;
    public bool IsShortcutsVisible => CurrentPage == SettingsPage.SHORTCUTS;
    public bool IsStylesVisible => CurrentPage == SettingsPage.STYLES;

    public string PageTitle => SettingsText.Get(CurrentPage switch
    {
        SettingsPage.SHORTCUTS => "Shortcuts",
        SettingsPage.STYLES => "Styles",
        _ => "Appearance"
    });

    public string? Error => externalError ?? (CurrentPage switch
    {
        SettingsPage.SHORTCUTS => Shortcuts.Error,
        SettingsPage.STYLES => Styles.Error,
        _ => null
    });

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public int PageIndex
    {
        get => pageIndex;
        set
        {
            if (!Enum.IsDefined((SettingsPage)value))
            {
                return;
            }

            if (SetProperty(ref pageIndex, value))
            {
                Shortcuts.IsRecording = false;
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(IsAppearanceVisible));
                OnPropertyChanged(nameof(IsShortcutsVisible));
                OnPropertyChanged(nameof(IsStylesVisible));
                OnPropertyChanged(nameof(PageTitle));
                RefreshError();
            }
        }
    }

    /// <summary>外部存储或工程工作流返回错误时显示其结果。</summary>
    public void ShowError(string? message)
    {
        externalError = message;
        RefreshError();
    }

    /// <summary>更新展示语言，保留所有页面草稿。</summary>
    public void RefreshLanguage()
    {
        Appearance.RefreshLanguage();
        Shortcuts.RefreshLanguage();
        Styles.RefreshLanguage();
        title = SettingsText.Get("Settings");
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PageTitle));
        RefreshError();
    }

    private void PageModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Error))
        {
            externalError = null;
            RefreshError();
        }
    }

    private void RefreshError()
    {
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }
}
