using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace AegiNext.Media.Probing;

internal static class FfprobeToolchain
{
    internal static FfprobeToolIdentity ReadIdentity(string json, string executablePath, string sha256)
    {
        try
        {
            using var resource = typeof(FfprobeToolchain).Assembly.GetManifestResourceStream("AegiNext.Media.Probing.ffmpeg-toolchain.json")
                ?? throw new InvalidOperationException("缺少内嵌 FFmpeg 工具版本契约。");
            using var manifest = JsonDocument.Parse(resource);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.GetProperty("program_version").GetProperty("version").GetString()
                ?? throw new InvalidDataException("ffprobe 未报告程序版本。");
            if (!manifest.RootElement.GetProperty("acceptedVersionStrings").EnumerateArray().Any(item => item.GetString() == version))
            {
                throw new InvalidDataException($"不支持 ffprobe 版本 {version}；要求 {manifest.RootElement.GetProperty("version").GetString()} 的受支持构建。");
            }

            var libraries = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var library in root.GetProperty("library_versions").EnumerateArray())
            {
                var name = library.GetProperty("name").GetString() ?? throw new InvalidDataException("缺少媒体库名称。");
                var compiledVersion = string.Create(CultureInfo.InvariantCulture,
                    $"{library.GetProperty("major").GetInt32()}.{library.GetProperty("minor").GetInt32()}.{library.GetProperty("micro").GetInt32()}");
                var packedVersion = library.GetProperty("version").GetUInt32();
                var libraryVersion = string.Create(CultureInfo.InvariantCulture,
                    $"{packedVersion >> 16}.{(packedVersion >> 8) & 255}.{packedVersion & 255}");
                if (compiledVersion != libraryVersion)
                {
                    throw new InvalidDataException($"媒体库 {name} 编译版本 {compiledVersion} 与运行版本 {libraryVersion} 不一致。");
                }

                if (!libraries.TryAdd(name, libraryVersion))
                {
                    throw new InvalidDataException($"ffprobe 重复报告 {name}。");
                }
            }

            foreach (var expected in manifest.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (!libraries.TryGetValue(expected.Name, out var actual) || actual != expected.Value.GetString())
                {
                    throw new InvalidDataException($"媒体库 {expected.Name} 版本不匹配：{actual ?? "未报告"}，要求 {expected.Value.GetString()}。");
                }
            }

            return new(executablePath, sha256, version, libraries.ToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("ffprobe 工具身份报告无效。", exception);
        }
    }
}
