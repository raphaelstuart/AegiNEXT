using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>版本化 JSON 工程存储；同目录临时文件落盘成功后才原子替换目标。</summary>
public static class ProjectStore
{
    private const int MAXIMUM_BYTES = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions options = CreateOptions();

    /// <summary>加载有限大小工程；重复键、未知字段、缺少必需字段与非法版本全部拒绝。</summary>
    public static async Task<ProjectDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > MAXIMUM_BYTES)
        {
            throw new InvalidDataException("项目文件超过 32 MiB。");
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MAXIMUM_BYTES)
            {
                throw new InvalidDataException("项目文件超过 32 MiB。");
            }

            buffer.Write(chunk, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Deserialize(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    /// <summary>先验证快照并完整写入临时文件；提交前取消或失败不修改已有工程。</summary>
    public static Task SaveAsync(ProjectDocument document, string path, CancellationToken cancellationToken = default)
        => WriteAsync(document, path, true, cancellationToken);

    /// <summary>原子提交新工程文件；已有文件或目录永不覆盖。</summary>
    public static Task CreateAsync(ProjectDocument document, string path, CancellationToken cancellationToken = default)
        => WriteAsync(document, path, false, cancellationToken);

    private static async Task WriteAsync(ProjectDocument document, string path, bool overwrite,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Serialize(document);
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
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>输出可保存的 UTF-8 JSON 字节，时间保持有理表示。</summary>
    public static byte[] Serialize(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        var result = JsonSerializer.SerializeToUtf8Bytes(document, options);
        if (result.Length > MAXIMUM_BYTES)
        {
            throw new InvalidDataException("项目文件超过 32 MiB。");
        }

        return result;
    }

    /// <summary>解析 UTF-8 JSON，并统一报告工程格式错误。</summary>
    public static ProjectDocument Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.Length > MAXIMUM_BYTES)
        {
            throw new InvalidDataException("项目文件超过 32 MiB。");
        }

        try
        {
            using var parsed = JsonDocument.Parse(json.ToArray(), new() { MaxDepth = 128 });
            RejectDuplicateKeys(parsed.RootElement);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number is not (3 or 4 or ProjectDocument.CURRENT_VERSION))
            {
                throw new InvalidDataException($"只支持项目版本 3、4 和 {ProjectDocument.CURRENT_VERSION}，更旧项目需要使用对应版本打开。");
            }

            var content = JsonNode.Parse(parsed.RootElement.GetRawText(), documentOptions: new() { MaxDepth = 128 })!.AsObject();
            if (number is 3 or 4)
            {
                SubtitleContentJsonMigration.UpgradeProject(content, number);
                ClipMaskJsonMigration.UpgradeLegacy(content);
                using var normalized = JsonDocument.Parse(content.ToJsonString(new() { MaxDepth = 128 }), new() { MaxDepth = 128 });
                content = VectorAnimationJsonMigration.Upgrade(normalized.RootElement, options);
            }
            else
            {
                ClipMaskJsonMigration.RejectCurrentLegacyFields(content);
            }

            var document = content.Deserialize<ProjectDocument>(options) ?? throw new JsonException("项目不能为空。");
            return SubtitleKaraokeNormalization.Normalize(document);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("项目 JSON 格式无效。", error);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type.Namespace == typeof(ProjectDocument).Namespace && info.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = (property.Get is not null || property.Set is not null) &&
                        !(info.Type == typeof(ProjectDocument) && property.Name == "timelineViewState") &&
                        !(info.Type == typeof(SubtitleStyle) && property.Name == "fontVariant") &&
                        !(info.Type == typeof(SubtitleFontVariant) && property.Name == "postScriptName") &&
                        !(info.Type == typeof(SubtitleInlineStyleOverride) && property.Name is "fontVariant" or "clearFontVariant") &&
                        !(info.Type == typeof(Keyframe) && property.Name is "componentCurves" or "exponent") &&
                        !(info.Type == typeof(AnimationCurve) && property.Name == "exponent") &&
                        !(info.Type == typeof(AnimationTrack) && property.Name is "initialValue" or "transforms") &&
                        !(info.Type == typeof(SubtitleLine) && property.Name is "karaokeStyle" or "inactiveKaraoke" or "styleName") &&
                        !(info.Type == typeof(SubtitleTrack) && property.Name is "defaultStyle" or "stylePresetId" or "stylePresetName" or "autoApplyStyle");
                    if (info.Type == typeof(SubtitleLine) && property.Name == "inactiveKaraoke")
                    {
                        property.ShouldSerialize = static (instance, _) => !((SubtitleLine)instance).InactiveKaraoke.IsEmpty;
                    }
                }
            }
        });
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 128,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
        result.Converters.Add(new MediaTimeJsonConverter());
        result.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return result;
    }

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"重复的字段：{property.Name}");
                }

                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateKeys(item);
            }
        }
    }
}
