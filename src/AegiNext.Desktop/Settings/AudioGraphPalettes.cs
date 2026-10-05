namespace AegiNext.Desktop.Settings;

internal static class AudioGraphPalettes
{
    internal const int CUSTOM_INDEX = 4;
    private static readonly AudioGraphPalette[] palettes =
    [
        new(),
        new() { UseClassicSpectrum = false, Low = "#091725", Mid = "#21678C", High = "#D9F7FF", Waveform = "#84E7FF90" },
        new() { UseClassicSpectrum = false, Low = "#170C1C", Mid = "#B53758", High = "#FFE9A1", Waveform = "#FFF0B390" },
        new() { UseClassicSpectrum = false, Low = "#111111", Mid = "#777777", High = "#FFFFFF", Waveform = "#FFFFFF80" }
    ];
    private static readonly AudioGraphPalette[] lightPalettes =
    [
        new() { UseClassicSpectrum = false, Low = "#F2F5F8", Mid = "#5D85C1", High = "#132F59", Waveform = "#102943B0" },
        new() { UseClassicSpectrum = false, Low = "#F2FAFC", Mid = "#63B7C7", High = "#114E68", Waveform = "#0B465DB0" },
        new() { UseClassicSpectrum = false, Low = "#FFF6EF", Mid = "#EDB075", High = "#8D314A", Waveform = "#5C2F27B0" },
        new() { UseClassicSpectrum = false, Low = "#FAFAFA", Mid = "#B3B3B3", High = "#333333", Waveform = "#111111A0" }
    ];

    internal static AudioGraphPalette Get(int index) => palettes[index];

    internal static AudioGraphPalette Resolve(AudioGraphPalette value, bool isLightTheme)
    {
        var index = IndexOf(value);
        return isLightTheme && value.AdaptToTheme && index != CUSTOM_INDEX ? lightPalettes[index] : value;
    }

    internal static int IndexOf(AudioGraphPalette value)
    {
        var index = Array.IndexOf(palettes, value);
        return index < 0 ? CUSTOM_INDEX : index;
    }
}
