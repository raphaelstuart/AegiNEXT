namespace AegiNext.Core.Media;

/// <summary>
/// 保留来源色彩名称，包括未知或将来扩展的名称；不根据位深推断 HDR。
/// </summary>
public sealed record MediaColorInfo
{
    public string? Range { get; init; }

    public string? Matrix { get; init; }

    public string? Transfer { get; init; }

    public string? Primaries { get; init; }

    public string? ChromaLocation { get; init; }

    public bool IsPq => string.Equals(Transfer, "smpte2084", StringComparison.Ordinal);

    public bool IsHlg => string.Equals(Transfer, "arib-std-b67", StringComparison.Ordinal);
}
