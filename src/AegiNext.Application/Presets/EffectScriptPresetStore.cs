using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Core.Effects;

namespace AegiNext.Application.Presets;

/// <summary>个人脚本 JSON 库与 .aegifx 文本交换文件的严格、原子存储。</summary>
public static class EffectScriptPresetStore
{
    private const int MAXIMUM_FILE_BYTES = 8 * 1024 * 1024;
    private static readonly UTF8Encoding utf8 = new(false, true);
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8
    };

    /// <summary>完整读取、验证个人库，不修改磁盘或调用方快照。</summary>
    public static async Task<EffectScriptPresetDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await PresetFileReader.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        try
        {
            _ = utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
            ValidateKeys(document.RootElement);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out _) || !root.TryGetProperty("presets", out _))
            {
                throw new JsonException("脚本库缺少版本或模板集合。");
            }

            var collection = root.Deserialize<EffectScriptPresetDocument>(options) ?? throw new JsonException("脚本库不能为 null。");
            EffectScriptPresetService.Validate(collection);
            return collection;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        {
            throw new InvalidDataException("特效脚本库不是有效的 UTF-8 JSON。", error);
        }
    }

    /// <summary>验证后原子保存，取消、失败或非法输入保留原文件。</summary>
    public static Task SaveAsync(EffectScriptPresetDocument collection, string path, CancellationToken cancellationToken = default)
    {
        EffectScriptPresetService.Validate(collection);
        return WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(collection, options), cancellationToken);
    }

    /// <summary>读取单个可交换脚本，采用严格 UTF-8 并返回解析后的同一份源。</summary>
    public static async Task<EffectScriptTemplate> ReadScriptAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await PresetFileReader.ReadAsync(path, EffectScriptParser.MAXIMUM_SOURCE_CHARACTERS * 4, cancellationToken).ConfigureAwait(false);
        string source;
        try
        {
            source = utf8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("特效脚本必须使用 UTF-8 编码。", error);
        }

        return new(source, EffectScriptParser.Parse(source));
    }

    /// <summary>导出内置或个人脚本的原文，格式为独立 UTF-8 .aegifx 文件。</summary>
    public static Task WriteScriptAsync(string source, string path, CancellationToken cancellationToken = default)
    {
        _ = EffectScriptParser.Parse(source);
        return WriteAtomicAsync(path, utf8.GetBytes(source), cancellationToken);
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("特效脚本文件超过 8 MiB。");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void ValidateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"重复的字段：{property.Name}。");
                }

                ValidateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateKeys(item);
            }
        }
    }
}
