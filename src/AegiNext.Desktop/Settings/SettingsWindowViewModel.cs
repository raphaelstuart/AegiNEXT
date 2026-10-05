using System.ComponentModel;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings.Appearance;
using AegiNext.Desktop.Settings.Colors;
using AegiNext.Desktop.Settings.Effects;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Settings.Styles;
using AegiNext.Desktop.Settings.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings;

/// <summary>设置导航和独立页面的组合模型，不拥有工程或控件。</summary>
public sealed class SettingsWindowViewModel : ObservableObject
{
    private int pageIndex;
    private string? externalError;
    private string title = Localization.Get("Settings.Settings");

    /// <summary>使用已加载偏好构造页面模型。</summary>
    public SettingsWindowViewModel(WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        Appearance = new(preferences);
        Colors = new(preferences);
        Shortcuts = new(preferences.ShortcutBindings);
        Styles = new();
        Effects = new();
        Media = new(preferences);
        Shortcuts.PropertyChanged += PageModelChanged;
        Styles.PropertyChanged += PageModelChanged;
        Effects.PropertyChanged += PageModelChanged;
    }

    public AppearanceSettingsViewModel Appearance { get; }
    public ColorsSettingsViewModel Colors { get; }
    public ShortcutSettingsViewModel Shortcuts { get; }
    public StyleSettingsViewModel Styles { get; }
    public EffectSettingsViewModel Effects { get; }
    public MediaSettingsViewModel Media { get; }
    public string Title => title;
    public SettingsPage CurrentPage => (SettingsPage)PageIndex;
    public bool IsAppearanceVisible => CurrentPage == SettingsPage.APPEARANCE;
    public bool IsShortcutsVisible => CurrentPage == SettingsPage.SHORTCUTS;
    public bool IsStylesVisible => CurrentPage == SettingsPage.STYLES;
    public bool IsEffectsVisible => CurrentPage == SettingsPage.EFFECTS;
    public bool IsColorsVisible => CurrentPage == SettingsPage.COLORS;
    public bool IsMediaVisible => CurrentPage == SettingsPage.MEDIA;

    public string PageTitle => Localization.Get("Settings." + (CurrentPage switch
    {
        SettingsPage.SHORTCUTS => "Shortcuts",
        SettingsPage.STYLES => "Styles",
        SettingsPage.EFFECTS => "Effects",
        SettingsPage.COLORS => "Colors",
        SettingsPage.MEDIA => "Media",
        _ => "Appearance"
    }));

    public string? Error => CurrentPage == SettingsPage.EFFECTS ? null : externalError ?? (CurrentPage switch
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
                OnPropertyChanged(nameof(IsEffectsVisible));
                OnPropertyChanged(nameof(IsColorsVisible));
                OnPropertyChanged(nameof(IsMediaVisible));
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
        Colors.RefreshLanguage();
        Shortcuts.RefreshLanguage();
        Styles.RefreshLanguage();
        Effects.RefreshLanguage();
        Media.RefreshLanguage();
        title = Localization.Get("Settings.Settings");
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
