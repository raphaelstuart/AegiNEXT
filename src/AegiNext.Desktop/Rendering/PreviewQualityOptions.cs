using AegiNext.Desktop.Settings;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal static class PreviewQualityOptions
{
    private static readonly SdrPreviewOptions low = new(960, 540);
    private static readonly SdrPreviewOptions standard = new(1280, 720);
    private static readonly SdrPreviewOptions high = new(1920, 1080);

    internal static SdrPreviewOptions Get(PreviewQuality quality, bool interactive = false)
    {
        if (!Enum.IsDefined(quality))
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }

        return (interactive ? PreviewQuality.LOW : quality) switch
        {
            PreviewQuality.LOW => low,
            PreviewQuality.STANDARD => standard,
            PreviewQuality.HIGH => high,
            _ => throw new ArgumentOutOfRangeException(nameof(quality))
        };
    }
}
