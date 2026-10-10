using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AegiNext.Core.Projects;

namespace AegiNext.Application.ColorTags;

/// <summary>严格验证个人颜色标记 JSON，并在完整写入后原子提交。</summary>
public static class SubtitleColorTagStore
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FILE_BYTES = 8 * 1024 * 1024;
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions options = CreateOptions();

    /// <summary>完整读取并验证库；文件不存在时由调用方决定初始化策略。</summary>
    public static async Task<SubtitleColorTagLibraryDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("颜色标记库超过大小预算。");
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MAXIMUM_FILE_BYTES)
            {
                throw new InvalidDataException("颜色标记库超过大小预算。");
            }
            buffer.Write(chunk, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Deserialize(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    /// <summary>写入同目录临时文件并原子替换；提交前失败或取消保留旧文件。</summary>
    public static Task SaveAsync(SubtitleColorTagLibraryDocument document, string path, CancellationToken cancellationToken = default)
    {
        return SaveAsync(document, path, null, cancellationToken);
    }

    /// <summary>完整准备并落盘后调用提交边界，然后原子替换库文件。</summary>
    public static async Task SaveAsync(SubtitleColorTagLibraryDocument document, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
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
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>验证并生成完整 UTF-8 JSON，拒绝超出大小预算的库。</summary>
    public static byte[] Serialize(SubtitleColorTagLibraryDocument document)
    {
        Validate(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, options);
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("颜色标记库超过大小预算。");
        }
        return bytes;
    }

    /// <summary>拒绝无效 UTF-8、未知或重复字段、缺失字段及无效定义。</summary>
    public static SubtitleColorTagLibraryDocument Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("颜色标记库超过大小预算。");
        }
        try
        {
            _ = strictUtf8.GetCharCount(json);
            var document = JsonSerializer.Deserialize<SubtitleColorTagLibraryDocument>(json, options)
                ?? throw new JsonException("颜色标记库不能为空。");
            Validate(document);
            return document;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("颜色标记库 JSON 无效。", error);
        }
    }

    internal static void Validate(SubtitleColorTagLibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != 1)
        {
            throw new InvalidDataException("颜色标记库版本不受支持。");
        }
        SubtitleColorTagValidator.Validate(document.Tags);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = property.Get is not null && property.Set is not null;
                }
            }
        });
        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 8,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
    }
}
