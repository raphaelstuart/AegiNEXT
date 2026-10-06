using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class UiPreviewConverter : IVideoPreviewConverter
{
    private bool disposed;

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var marker = frame.CopyPlane(0)[0];
        return new(1, 1, [marker, marker, marker, 255]);
    }

    public void Dispose()
    {
        disposed = true;
    }
}
