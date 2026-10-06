using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class MaskNumericField(string key, string labelKey, AnimationTrackTarget? target, int component, double minimum, double maximum)
{
    public string Key { get; } = key;
    public string Label => Localization.Get(labelKey) + (Target is { } identity && AnimationPropertyMetadata.GetComponentCount(identity.Property) > 1 ? " " + AnimationPropertyMetadata.GetComponentName(identity.Property, Component) : string.Empty);
    public AnimationTrackTarget? Target { get; } = target;
    public int Component { get; } = component;
    public decimal Minimum { get; } = (decimal)minimum;
    public bool CanEdit { get; init; } = true;
    public decimal Maximum { get; } = (decimal)maximum;
    public NumericValueDraft Draft { get; } = new();
    internal double Original { get; set; }
    internal bool IsDirty => Draft.RawText != ((decimal)Original).ToString(System.Globalization.CultureInfo.CurrentCulture);
}
