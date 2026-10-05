namespace AegiNext.Desktop.Diagnostics;

internal sealed record AutoHeightDialogProbeSample(string Scenario, string Sizing, double ClientHeight,
    double DesiredHeight, double BottomGap, string? PlatformFailure);
