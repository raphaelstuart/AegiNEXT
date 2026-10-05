using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Panels.Preview;

internal sealed record PreviewQualityChoice(PreviewQuality Id, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
