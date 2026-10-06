using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>仅属于字幕片段的工程画面坐标蒙版，独立于字幕及父图层的变换。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RectangleClipMask), "RECTANGLE")]
[JsonDerivedType(typeof(VectorClipMask), "VECTOR")]
public abstract record ClipMask
{
    public bool Inverted { get; init; }
    public MaskTransform Transform { get; init; } = new();
}
