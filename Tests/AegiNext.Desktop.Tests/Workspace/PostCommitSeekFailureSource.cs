using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class PostCommitSeekFailureSource : IVideoFrameSource
{
    private readonly PreviewTestSource source = new(7, 0, 100, 200);

    internal IOException Failure { get; } = new("The committed project could not seek its preview.");
    internal int DisposeCount => source.DisposeCount;

    /// <inheritdoc />
    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        return source.ReadFrame(cancellationToken);
    }

    /// <inheritdoc />
    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw Failure;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        source.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        source.Dispose();
    }
}
