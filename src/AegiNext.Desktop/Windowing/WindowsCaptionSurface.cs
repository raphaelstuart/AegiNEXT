using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AegiNext.Desktop.Windowing;

internal sealed class WindowsCaptionSurface : IDisposable
{
    private readonly Window window;
    private readonly Geometry? originalClip;
    private readonly IReadOnlyList<WindowTransparencyLevel> originalTransparency;
    private Geometry? appliedClip;
    private Size clientSize;
    private bool disposed;

    internal WindowsCaptionSurface(Window window)
    {
        this.window = window;
        originalClip = window.Clip;
        originalTransparency = window.TransparencyLevelHint;
        window.SetCurrentValue(TopLevel.TransparencyLevelHintProperty,
            new[] { WindowTransparencyLevel.Transparent });
    }

    internal Rect Aperture { get; private set; }

    internal void Update(Rect aperture)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var size = window.ClientSize;
        aperture = aperture.Intersect(new Rect(size));
        if (size == clientSize && aperture == Aperture)
        {
            return;
        }

        clientSize = size;
        Aperture = aperture;
        Geometry? clip = originalClip;
        if (aperture.Width > 0 && aperture.Height > 0)
        {
            clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(size)), new RectangleGeometry(aperture));
            if (originalClip is not null)
            {
                clip = new CombinedGeometry(GeometryCombineMode.Intersect, originalClip, clip);
            }
        }

        appliedClip = clip;
        window.SetCurrentValue(Visual.ClipProperty, clip);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ReferenceEquals(window.Clip, appliedClip))
        {
            window.SetCurrentValue(Visual.ClipProperty, originalClip);
        }

        window.SetCurrentValue(TopLevel.TransparencyLevelHintProperty, originalTransparency);
    }
}
