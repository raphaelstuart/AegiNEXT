using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>资源导入与另存为重定位；媒体按工程目录选择相对或外部引用，字体和图片进入资源目录。</summary>
public static class ProjectResources
{
    /// <summary>以工程目录解析源路径，导入媒体引用或复制字体／图片；复制目标使用内容哈希命名。</summary>
    public static async Task<ProjectAsset> ImportAsync(string sourcePath, ProjectAssetKind kind, string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var source = ProjectAssetLocation.ResolveInputPath(sourcePath, projectDirectory);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("资源不存在。", source);
        }

        if (kind == ProjectAssetKind.MEDIA)
        {
            return RelocateMedia(new(Guid.NewGuid(), kind, string.Empty), source, projectDirectory);
        }

        var directory = Path.Combine(Path.GetFullPath(projectDirectory), "assets");
        Directory.CreateDirectory(directory);
        if (new DirectoryInfo(directory).LinkTarget is not null)
        {
            throw new InvalidDataException("资源目录不能是符号链接。");
        }

        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] hash;
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, true))
            {
                if (input.Length > 512L * 1024 * 1024)
                {
                    throw new InvalidDataException("字体或图片资源超过 512 MiB。");
                }

                var buffer = new byte[65536];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > 512L * 1024 * 1024)
                    {
                        throw new InvalidDataException("字体或图片资源超过 512 MiB。");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Position = 0;
                hash = await SHA256.HashDataAsync(output, cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }

            var digest = Convert.ToHexStringLower(hash);
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension.Length > 16 || extension.Any(character => character != '.' && !char.IsAsciiLetterOrDigit(character)))
            {
                extension = ".bin";
            }

            var relative = $"assets/{digest}{extension}";
            var destination = Path.Combine(directory, digest + extension);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination))
            {
                if (new FileInfo(destination).LinkTarget is not null)
                {
                    throw new InvalidDataException("资源文件不能是符号链接。");
                }

                await using var existing = File.OpenRead(destination);
                var existingHash = await SHA256.HashDataAsync(existing, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(hash, existingHash))
                {
                    throw new InvalidDataException("已存在同名但内容不同的工程资源。");
                }
            }
            else
            {
                File.Move(temporary, destination);
            }

            return new(Guid.NewGuid(), kind, relative, digest);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>规范化工程目录内的媒体引用；不访问文件系统，不变时保留原快照。</summary>
    public static ProjectDocument NormalizeMediaReferences(ProjectDocument document, string projectDirectory)
    {
        ProjectValidator.Validate(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ImmutableArray<ProjectAsset>.Builder? assets = null;
        for (var index = 0; index < document.Assets.Length; index++)
        {
            var asset = document.Assets[index];
            if (asset.Kind != ProjectAssetKind.MEDIA)
            {
                continue;
            }

            var normalized = RebaseMedia(asset, projectDirectory, projectDirectory);
            if (normalized != asset)
            {
                assets ??= document.Assets.ToBuilder();
                assets[index] = normalized;
            }
        }

        if (assets is null)
        {
            return document;
        }

        var result = document with { Assets = assets.ToImmutable() };
        ProjectValidator.Validate(result);
        return result;
    }

    /// <summary>为另存为复制托管小资源，并按新工程目录重定位媒体引用；原工程和旧资源保持不变。</summary>
    public static async Task<ProjectDocument> RebaseAsync(ProjectDocument document, string oldDirectory, string newDirectory,
        CancellationToken cancellationToken = default)
    {
        ProjectValidator.Validate(document);
        var assets = ImmutableArray.CreateBuilder<ProjectAsset>(document.Assets.Length);
        foreach (var asset in document.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (asset.Kind == ProjectAssetKind.MEDIA)
            {
                assets.Add(RebaseMedia(asset, oldDirectory, newDirectory));
                continue;
            }

            var source = ProjectAssetLocation.Resolve(asset, oldDirectory);
            var imported = await ImportAsync(source, asset.Kind, newDirectory, cancellationToken).ConfigureAwait(false);
            if (asset.Sha256 is { } expected &&
                !string.Equals(expected, imported.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("资源内容与工程记录的 SHA-256 不一致。");
            }

            assets.Add(imported with { Id = asset.Id });
        }

        var result = document with { Assets = assets.ToImmutable() };
        ProjectValidator.Validate(result);
        return result;
    }

    private static ProjectAsset RebaseMedia(ProjectAsset asset, string oldDirectory, string newDirectory)
    {
        if (asset.ExternalPath is { } external && !Path.IsPathFullyQualified(external))
        {
            return asset;
        }

        return RelocateMedia(asset, ProjectAssetLocation.Resolve(asset, oldDirectory), newDirectory);
    }

    private static ProjectAsset RelocateMedia(ProjectAsset asset, string source, string projectDirectory)
    {
        return ProjectAssetLocation.TryGetRelativePath(source, projectDirectory, out var relativePath)
            ? asset with { RelativePath = relativePath, ExternalPath = null }
            : asset with { RelativePath = string.Empty, ExternalPath = source };
    }
}
