namespace AegiNext.Desktop.Diagnostics;

internal sealed record WorkspaceProbeSample(string Action, string WindowKind, string Title,
    double ClientWidth, double ClientHeight, bool ExtendedClientArea, string Decorations,
    bool WindowMenuVisible, bool NativeMenuExported, double CaptionInsetLeft, double CaptionInsetRight,
    int? VisibleMacOsSystemButtons, string PresetId, bool Modified);
