using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisCacheSnapshot : IDisposable
{
    private readonly SafeFileHandle original;
    private int disposed;

    internal AudioAnalysisCacheSnapshot(SafeFileHandle handle, long length, byte[] index)
    {
        original = handle;
        var retained = false;
        original.DangerousAddRef(ref retained);
        Handle = new(original.DangerousGetHandle(), false);
        Length = length;
        Index = index;
    }

    internal SafeFileHandle Handle { get; }
    internal long Length { get; }
    internal ReadOnlyMemory<byte> Index { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            Handle.Dispose();
            original.DangerousRelease();
        }
    }
}
