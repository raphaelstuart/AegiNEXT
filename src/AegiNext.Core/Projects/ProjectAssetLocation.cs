namespace AegiNext.Core.Projects;

/// <summary>统一解释工程资源位置；相对路径不离开工程目录，外部媒体可在异机重新绑定。</summary>
public static class ProjectAssetLocation
{
    /// <summary>解析本机可使用的路径；异平台绝对路径保留在工程中，但不能在当前平台解析。</summary>
    public static string Resolve(ProjectAsset asset, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        if (asset.ExternalPath is { } external)
        {
            if (asset.Kind != ProjectAssetKind.MEDIA || asset.RelativePath != string.Empty || !IsAbsoluteReference(external))
            {
                throw new InvalidDataException("非法外部媒体资源。");
            }

            if (!Path.IsPathFullyQualified(external))
            {
                throw new NotSupportedException("外部媒体路径属于其他平台，请重新绑定媒体。");
            }

            return Path.GetFullPath(external);
        }

        ProjectValidator.ValidateRelativePath(asset.RelativePath);
        return Path.GetFullPath(Path.Combine(projectDirectory, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>识别 Unix、Windows 盘符与 UNC 绝对引用，不访问文件系统。</summary>
    public static bool IsAbsoluteReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32768 || value.Any(char.IsControl))
        {
            return false;
        }

        return value.StartsWith('/') || value.StartsWith("\\\\", StringComparison.Ordinal) ||
            value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '/' or '\\';
    }
}
