namespace AegiNext.Desktop.Diagnostics;

internal sealed record WindowChromeProbeSample(
    string Action,
    string Host,
    string Title,
    double ClientWidth,
    double ClientHeight,
    double RenderScaling,
    double CaptionInsetLeft,
    double CaptionInsetRight,
    bool ExtendedClientArea,
    string Decorations,
    string WindowState,
    bool WindowMenuVisible,
    int? VisibleMacOsSystemButtons,
    bool? NativeCaptionButtonsMeasured,
    string? PlatformFailure,
    bool? NativeNonClientRenderingEnabled = null,
    string? NativeCaptionTransparency = null,
    double? NativeCaptionApertureWidth = null,
    double? NativeCaptionApertureHeight = null);
