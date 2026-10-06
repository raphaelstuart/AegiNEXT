namespace AegiNext.Desktop.Settings;

internal static class TimelineClipPalettes
{
    private static readonly TimelineClipPalette darkPalette = new();
    private static readonly TimelineClipPalette lightPalette = new()
    {
        SelectedClip = "#BCD2FAE4",
        InactiveClip = "#ADB7C233",
        StartLine = "#168653",
        EndLine = "#BA3D50",
        SelectedRangeFill = "#5273E818",
        InactiveRangeFill = "#6B748010"
    };

    internal static TimelineClipPalette Resolve(TimelineClipPalette value, bool isLightTheme)
    {
        return isLightTheme && value.AdaptToTheme && value == darkPalette ? lightPalette : value;
    }
}
