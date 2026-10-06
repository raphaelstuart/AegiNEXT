using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Settings;

/// <summary>个人音频图配色，HEX 使用 sRGB 和 RGBA 后缀顺序。</summary>
public sealed record AudioGraphPalette
{
    public bool UseClassicSpectrum { get; init; } = true;
    public bool AdaptToTheme { get; init; } = true;
    public string Low { get; init; } = "#121C16";
    public string Mid { get; init; } = "#2F53AC";
    public string High { get; init; } = "#FFF816";
    public string Waveform { get; init; } = "#79D5DB25";

    /// <summary>验证不透明语谱色和带透明度的波形色，拒绝损坏偏好。</summary>
    public void Validate()
    {
        foreach (var text in new[] { Low, Mid, High })
        {
            if (text is null || !ColorHexCodec.TryParse(text, 1, true, out var color) || color.Alpha != 1)
            {
                throw new InvalidDataException("语谱图颜色必须是有效的不透明 HEX 颜色。");
            }
        }

        if (Waveform is null || !ColorHexCodec.TryParse(Waveform, 1, true, out _))
        {
            throw new InvalidDataException("波形颜色必须是有效的 HEX 颜色。");
        }
    }
}
