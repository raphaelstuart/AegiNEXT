using AegiNext.Desktop.I18n;
using AegiNext.Media.Decoding;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Media;

/// <summary>媒体偏好与解码状态；媒体会话的重建和保存由工作台协调。</summary>
public sealed partial class MediaSettingsViewModel : ObservableObject
{
    private bool updating;
    private bool isBusy;
    private VideoDecodeMode selectedMode;
    private readonly VideoDecodeModeChoice[] decodeModes =
    [
        new(VideoDecodeMode.Auto), new(VideoDecodeMode.Software), new(VideoDecodeMode.Hardware)
    ];
    private string? backend;
    private bool hardwareAccelerated;
    private string? fallbackReason;

    /// <summary>从已经验证的偏好创建媒体页面。</summary>
    public MediaSettingsViewModel(WorkbenchPreferences preferences)
    {
        UpdatePreferences(preferences);
    }

    public event EventHandler<SettingsPreviewDecodeModeChangedEventArgs>? DecodeModeChanged;
    public IReadOnlyList<VideoDecodeModeChoice> DecodeModes => decodeModes;
    public bool CanChangeDecodeMode => !IsBusy;
    public string DecodeDescription => Localization.Get(selectedMode switch
    {
        VideoDecodeMode.Software => "Settings.DecodeSoftwareDescription",
        VideoDecodeMode.Hardware => "Settings.DecodeHardwareDescription",
        _ => "Settings.DecodeAutoDescription"
    });

    public string DecodeStatus => IsBusy
        ? Localization.Get("Settings.DecodeSwitching")
        : backend is null
            ? Localization.Get("Settings.DecodeNoMedia")
            : string.IsNullOrWhiteSpace(fallbackReason)
                ? Localization.Format("Settings.DecodeActive", hardwareAccelerated ? "GPU" : "CPU", backend)
                : Localization.Format("Settings.DecodeFallback", backend, fallbackReason);

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanChangeDecodeMode));
                OnPropertyChanged(nameof(DecodeStatus));
                OnPropertyChanged(nameof(CanCalibrate));
            }
        }
    }

    public VideoDecodeModeChoice? SelectedDecodeMode
    {
        get => decodeModes.FirstOrDefault(choice => choice.Mode == selectedMode);
        set
        {
            if (value is null || !Enum.IsDefined(value.Mode) || value.Mode == selectedMode || IsBusy)
            {
                return;
            }

            selectedMode = value.Mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DecodeDescription));
            if (!updating)
            {
                DecodeModeChanged?.Invoke(this, new(selectedMode));
            }
        }
    }

    /// <summary>同步已应用偏好，回填不产生用户请求。</summary>
    public void UpdatePreferences(WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        if (!IsBusy)
        {
            selectedMode = preferences.PreviewDecodeMode;
        }

        RefreshLanguage();
        UpdateCalibrationPreferences(preferences);
    }

    /// <summary>同步实际后端证据，不从请求的模式推断 GPU 是否启用。</summary>
    public void UpdateDecodeStatus(string? activeBackend, bool isHardwareAccelerated, string? automaticFallbackReason)
    {
        backend = activeBackend;
        hardwareAccelerated = isHardwareAccelerated;
        fallbackReason = automaticFallbackReason;
        OnPropertyChanged(nameof(DecodeStatus));
    }

    /// <summary>翻译选项并保留稳定模式，不触发切换。</summary>
    public void RefreshLanguage()
    {
        var previousMode = selectedMode;
        updating = true;
        try
        {
            decodeModes[0].UpdateLabel(Localization.Get("Settings.DecodeAuto"));
            decodeModes[1].UpdateLabel(Localization.Get("Settings.DecodeSoftware"));
            decodeModes[2].UpdateLabel(Localization.Get("Settings.DecodeHardware"));
            OnPropertyChanged(nameof(DecodeModes));
            selectedMode = previousMode;
            OnPropertyChanged(nameof(SelectedDecodeMode));
            OnPropertyChanged(nameof(DecodeDescription));
            OnPropertyChanged(nameof(DecodeStatus));
            RefreshAudioLanguage();
        }
        finally
        {
            updating = false;
        }
    }
}
