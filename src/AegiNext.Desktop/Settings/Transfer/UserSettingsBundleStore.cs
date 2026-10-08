using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AegiNext.Application.Presets;
using AegiNext.Desktop.Layouts;
using AegiNext.Media.Encoding.Presets;

namespace AegiNext.Desktop.Settings.Transfer;

internal static class UserSettingsBundleStore
{
    internal const int MAXIMUM_FILE_BYTES = 128 * 1024 * 1024;
    private const string MANIFEST_NAME = "manifest.json";
    private const int MAXIMUM_MANIFEST_BYTES = 1024;
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    internal static ImmutableArray<UserSettingsBundleFile> Files { get; } =
    [
        new("preferences.json", WorkbenchPreferencesStore.MAXIMUM_FILE_BYTES),
        new("subtitle-styles.aegistyles", SubtitleStylePresetStore.MAXIMUM_FILE_BYTES),
        new("effect-scripts.json", EffectScriptPresetStore.MAXIMUM_FILE_BYTES),
        new("export-presets.aegiexports", VideoExportPresetStore.MAXIMUM_FILE_BYTES),
        new("layouts.json", WorkspaceLayoutStore.MAXIMUM_FILE_BYTES)
    ];

    internal static byte[] Serialize(UserSettingsBundle bundle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = SerializeFiles(bundle, cancellationToken);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, MANIFEST_NAME, "{\"version\":1}"u8, cancellationToken);
            foreach (var file in Files)
            {
                WriteEntry(archive, file.Name, files[file.Name], cancellationToken);
            }
        }
        if (buffer.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("用户设置包超过 128 MiB。");
        }
        return buffer.ToArray();
    }

    internal static UserSettingsBundle Deserialize(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("用户设置包超过 128 MiB。");
        }
        try
        {
            using var buffer = new MemoryStream(bytes.ToArray(), false);
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            if (archive.Entries.Count != Files.Length + 1)
            {
                throw new InvalidDataException("用户设置包必须包含清单和五个完整设置文件。");
            }
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var maximum = entry.FullName == MANIFEST_NAME ? MAXIMUM_MANIFEST_BYTES :
                    Files.FirstOrDefault(file => file.Name == entry.FullName)?.MaximumBytes;
                if (maximum is null || !entries.TryAdd(entry.FullName, entry) || entry.Length > maximum.Value)
                {
                    throw new InvalidDataException("用户设置包包含未知、重复、带路径或超出预算的文件。");
                }
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MAXIMUM_FILE_BYTES)
                {
                    throw new InvalidDataException("用户设置包解压内容超过 128 MiB。");
                }
            }
            ValidateManifest(ReadEntry(entries[MANIFEST_NAME], MAXIMUM_MANIFEST_BYTES, cancellationToken));
            var content = Files.ToDictionary(file => file.Name,
                file => ReadEntry(entries[file.Name], file.MaximumBytes, cancellationToken), StringComparer.Ordinal);
            return new()
            {
                Preferences = WorkbenchPreferencesStore.Deserialize(content["preferences.json"], allowForeignWorkspace: true),
                Styles = SubtitleStylePresetStore.Deserialize(content["subtitle-styles.aegistyles"]),
                Effects = EffectScriptPresetStore.Deserialize(content["effect-scripts.json"]),
                ExportPresets = VideoExportPresetStore.Deserialize(content["export-presets.aegiexports"]),
                Layouts = WorkspaceLayoutStore.Deserialize(content["layouts.json"])
            };
        }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException or
                                     KeyNotFoundException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("用户设置包格式无效。", error);
        }
    }

    internal static async Task<UserSettingsBundle> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await UserSettingsTransferFiles.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Deserialize(bytes, cancellationToken);
    }

    internal static Task SaveAsync(UserSettingsBundle bundle, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        return UserSettingsTransferFiles.WriteAtomicAsync(Path.GetFullPath(path), Serialize(bundle, cancellationToken), cancellationToken);
    }

    internal static Dictionary<string, byte[]> SerializeFiles(UserSettingsBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("preferences.json", WorkbenchPreferencesStore.Serialize(bundle.Preferences, allowForeignWorkspace: true));
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("subtitle-styles.aegistyles", SubtitleStylePresetStore.Serialize(bundle.Styles));
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("effect-scripts.json", EffectScriptPresetStore.Serialize(bundle.Effects));
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("export-presets.aegiexports", VideoExportPresetStore.Serialize(bundle.ExportPresets));
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("layouts.json", WorkspaceLayoutStore.Serialize(bundle.Layouts));
        cancellationToken.ThrowIfCancellationRequested();
        return files;
    }

    private static void WriteEntry(ZipArchive archive, string name, ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        const int CHUNK_SIZE = 65536;
        while (!bytes.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(bytes.Length, CHUNK_SIZE);
            stream.Write(bytes[..length]);
            bytes = bytes[length..];
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        var bytes = UserSettingsTransferFiles.Read(stream, maximumBytes, cancellationToken);
        if (bytes.LongLength != entry.Length)
        {
            throw new InvalidDataException("用户设置包文件长度与清单不一致。");
        }
        return bytes;
    }

    private static void ValidateManifest(ReadOnlySpan<byte> bytes)
    {
        _ = strictUtf8.GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 2 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("用户设置包清单无效。");
        }
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != "version" ||
            properties[0].Value.ValueKind != JsonValueKind.Number ||
            !properties[0].Value.TryGetInt32(out var version) || version != 1)
        {
            throw new InvalidDataException("用户设置包版本不受支持或清单包含未知、重复字段。");
        }
    }
}
