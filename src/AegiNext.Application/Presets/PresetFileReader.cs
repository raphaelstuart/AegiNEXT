namespace AegiNext.Application.Presets;

internal static class PresetFileReader
{
    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException("预设或字体文件超过容量预算。");
        }

        using var buffer = new MemoryStream(checked((int)stream.Length));
        var chunk = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("预设或字体文件超过容量预算。");
            }

            buffer.Write(chunk, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }
}
