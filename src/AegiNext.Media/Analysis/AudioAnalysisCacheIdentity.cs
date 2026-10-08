using System.Security.Cryptography;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

internal static class AudioAnalysisCacheIdentity
{
    private const int SAMPLE_BYTES = 65536;
    private const string ALGORITHM = "48k-mono-abi2-fir63-hann1024-hop256-log128-db80-wave512-v1";

    internal static string Create(string path, int streamIndex, MediaTimelineMapping mapping, MediaTime duration)
    {
        var information = new FileInfo(path);
        var length = information.Length;
        var modified = information.LastWriteTimeUtc;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var metadata = new MemoryStream();
        using (var writer = new BinaryWriter(metadata, System.Text.Encoding.UTF8, true))
        {
            writer.Write(ALGORITHM);
            writer.Write(length);
            writer.Write(modified.Ticks);
            writer.Write(streamIndex);
            writer.Write(mapping.Origin.Numerator);
            writer.Write(mapping.Origin.Denominator);
            writer.Write(duration.Numerator);
            writer.Write(duration.Denominator);
        }
        hash.AppendData(metadata.ToArray());
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, SAMPLE_BYTES, FileOptions.RandomAccess);
        var buffer = new byte[SAMPLE_BYTES];
        foreach (var offset in new[] { 0, Math.Max(0, (length - SAMPLE_BYTES) / 2), Math.Max(0, length - SAMPLE_BYTES) }.Distinct())
        {
            input.Position = offset;
            var count = (int)Math.Min(SAMPLE_BYTES, length - offset);
            input.ReadExactly(buffer.AsSpan(0, count));
            hash.AppendData(buffer.AsSpan(0, count));
        }
        information.Refresh();
        if (information.Length != length || information.LastWriteTimeUtc != modified)
        {
            throw new IOException("音频缓存指纹读取期间媒体文件发生变化。");
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
