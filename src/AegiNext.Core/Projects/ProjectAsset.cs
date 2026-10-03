namespace AegiNext.Core.Projects;

/// <summary>字体／图片使用规范工程相对路径；仅媒体可使用 ExternalPath 引用大型外部文件。</summary>
public sealed record ProjectAsset(Guid Id, ProjectAssetKind Kind, string RelativePath, string? Sha256 = null, string? ExternalPath = null);
