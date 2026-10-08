using AegiNext.Desktop.I18n;
using AegiNext.Media.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.AudioAnalysis;

/// <summary>将稳定窗函数标识映射到设置页显示名称。</summary>
public sealed class AudioSpectrumWindowChoice(AudioSpectrumWindow window) : ObservableObject
{
    public AudioSpectrumWindow Window { get; } = window;
    public string Label => Localization.Get("Settings.AudioWindow" + Window);

    /// <summary>更新窗函数标签，保留稳定标识。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Label));
    }
}
