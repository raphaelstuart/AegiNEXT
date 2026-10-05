using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Colors;

/// <summary>身份稳定的配色方案项，语言刷新只更新显示文字。</summary>
public sealed class AudioGraphPaletteChoice(int index) : ObservableObject
{
    private string label = string.Empty;

    public int Index { get; } = index;
    public string Label
    {
        get => label;
        internal set => SetProperty(ref label, value);
    }
}
