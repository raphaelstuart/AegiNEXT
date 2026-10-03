namespace AegiNext.Media.Audio;

internal static class NativeAudioError
{
    internal static void Check(int status, ReadOnlySpan<byte> error, CancellationToken cancellationToken = default)
    {
        if (status == 0)
        {
            return;
        }

        var end = error.IndexOf((byte)0);
        var message = $"音频后端错误 {status}：{System.Text.Encoding.UTF8.GetString(end < 0 ? error : error[..end])}";
        if (status == 6)
        {
            throw new OperationCanceledException(message, cancellationToken);
        }

        throw new InvalidDataException(message);
    }
}
