using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class CatchupPreviewConverter : IVideoPreviewConverter
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int blockedMarker = -1;

    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Block(byte marker) => Volatile.Write(ref blockedMarker, marker);
    internal void Release() => release.TrySetResult();

    /// <summary>暂停指定帧的转换，以验证过期画面叠层的真实工作台接线。</summary>
    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        var marker = frame.CopyPlane(0)[0];
        if (marker == Volatile.Read(ref blockedMarker))
        {
            Entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).GetAwaiter().GetResult();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(1, 1, [marker, marker, marker, 255]);
    }

    /// <inheritdoc />
    public void Dispose() => release.TrySetResult();
}
