namespace AegiNext.Desktop.Controls;

/// <summary>字体草稿的纯解析与查询规则，可由字段和宿主草稿共用。</summary>
public static class FontSelectionResolver
{
    /// <summary>将同家族的样式完整名称与家族别名分开，防止其他字重污染搜索。</summary>
    public static IReadOnlyList<FontPickerCandidate> NormalizeCandidates(IEnumerable<FontPickerCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var values = candidates.ToArray();
        var variantNames = values.Where(candidate => candidate.Selection.Variant is not null)
            .GroupBy(candidate => candidate.Selection.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(family => family.Key, family => family.Select(candidate => NormalizeName(candidate.DisplayName))
                .ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(values.Select(candidate => variantNames.TryGetValue(candidate.Selection.FamilyName, out var names)
            ? new FontPickerCandidate(candidate.Selection, candidate.Aliases.Where(alias => !names.Contains(NormalizeName(alias))))
            : candidate).ToArray());
    }

    /// <summary>解析完整显示名或家族别名；有效的未知输入保留为普通家族。</summary>
    public static bool TryResolve(IEnumerable<FontPickerCandidate> candidates, FontSelection current, string text,
        out FontSelection selection)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        if (value.Length == 0 || value.Length > 512 || value.Any(char.IsControl))
        {
            selection = default;
            return false;
        }

        if (string.Equals(value, current.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            selection = current;
            return true;
        }

        var values = candidates as IReadOnlyList<FontPickerCandidate> ?? candidates.ToArray();
        foreach (var candidate in values)
        {
            if (string.Equals(value, candidate.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                selection = candidate.Selection;
                return true;
            }
        }

        foreach (var candidate in values)
        {
            if (SearchNames(candidate).Any(name => string.Equals(value, name, StringComparison.OrdinalIgnoreCase)))
            {
                selection = candidate.Selection;
                return true;
            }
        }

        foreach (var candidate in values)
        {
            if (string.Equals(value, candidate.Selection.FamilyName, StringComparison.OrdinalIgnoreCase) ||
                candidate.Aliases.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                selection = new(candidate.Selection.FamilyName, isSystemFont: candidate.Selection.IsSystemFont);
                return true;
            }
        }

        selection = new(value);
        return true;
    }

    /// <summary>按显示名、家族别名及 Black/Heavy 常用名称查找候选。</summary>
    public static bool MatchesQuery(FontPickerCandidate candidate, string query)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(query);
        return SearchNames(candidate).Any(name => name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasSameFace(FontSelection left, FontSelection right) => GetFaceKey(left) == GetFaceKey(right);

    internal static (string FamilyName, string? VariantName, int Weight, int Width, bool Italic) GetFaceKey(FontSelection selection)
    {
        var familyName = selection.FamilyName.ToUpperInvariant();
        return selection.Variant is { } variant
            ? (familyName, NormalizeName(variant.Name), variant.Weight, variant.Width, variant.Italic)
            : (familyName, null, 0, 0, false);
    }

    private static IEnumerable<string> SearchNames(FontPickerCandidate candidate)
    {
        yield return candidate.DisplayName;
        foreach (var alias in candidate.Aliases)
        {
            yield return candidate.Selection.Variant is { } variant ? $"{alias} {variant.Name}" : alias;
        }

        if (candidate.Selection.Variant is not { } selected)
        {
            yield break;
        }

        var words = selected.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var aliasWords = words.Select(word => string.Equals(word, "Black", StringComparison.OrdinalIgnoreCase) ? "Heavy" :
            string.Equals(word, "Heavy", StringComparison.OrdinalIgnoreCase) ? "Black" : word).ToArray();
        if (words.SequenceEqual(aliasWords, StringComparer.OrdinalIgnoreCase))
        {
            yield break;
        }

        var variantAlias = string.Join(' ', aliasWords);
        yield return $"{candidate.Selection.FamilyName} {variantAlias}";
        foreach (var alias in candidate.Aliases)
        {
            yield return $"{alias} {variantAlias}";
        }
    }

    private static string NormalizeName(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
