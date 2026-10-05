using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace AegiNext.Desktop.I18n;

internal sealed class LocalizationCatalog
{
    internal const string DEFAULT_LANGUAGE_ID = "en-US";
    private readonly FrozenDictionary<string, LanguagePack> packs;

    private LocalizationCatalog(IEnumerable<LanguagePack> packs, IEnumerable<LocalizationDiagnostic> diagnostics)
    {
        this.packs = packs.ToFrozenDictionary(pack => pack.Info.LanguageID, StringComparer.OrdinalIgnoreCase);
        KnownLanguages = this.packs.Values.Select(pack => pack.Info)
            .OrderBy(info => info.LanguageID, StringComparer.Ordinal).ToImmutableArray();
        Diagnostics = diagnostics.ToImmutableArray();
    }

    internal IReadOnlyList<LanguageInfo> KnownLanguages { get; }
    internal IReadOnlyList<LocalizationDiagnostic> Diagnostics { get; }

    internal static LocalizationCatalog Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var loaded = new List<(string Path, LanguagePack Pack)>();
        var diagnostics = new List<LocalizationDiagnostic>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            try
            {
                loaded.Add((path, LanguagePackReader.Read(path)));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                diagnostics.Add(new(path, error.Message));
            }
        }

        var accepted = new List<LanguagePack>();
        foreach (var group in loaded.GroupBy(entry => entry.Pack.Info.LanguageID, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                foreach (var entry in group)
                {
                    diagnostics.Add(new(entry.Path, $"Duplicate language identifier: {group.Key}."));
                }
            }
            else
            {
                accepted.Add(group.Single().Pack);
            }
        }

        if (!accepted.Any(pack => string.Equals(pack.Info.LanguageID, DEFAULT_LANGUAGE_ID, StringComparison.OrdinalIgnoreCase)))
        {
            var detail = string.Join(Environment.NewLine, diagnostics.Select(value => $"{value.FilePath}: {value.Message}"));
            throw new InvalidDataException($"A valid, unique {DEFAULT_LANGUAGE_ID} language pack is required in '{directory}'.{Environment.NewLine}{detail}");
        }

        return new(accepted, diagnostics);
    }

    internal LanguagePack GetLanguage(string languageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        return packs.TryGetValue(languageId, out var pack)
            ? pack
            : throw new ArgumentException($"The language '{languageId}' is not installed.", nameof(languageId));
    }

    internal string Get(string languageId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var pack = GetLanguage(languageId);
        return pack.Strings.TryGetValue(key, out var text) || packs[DEFAULT_LANGUAGE_ID].Strings.TryGetValue(key, out text)
            ? text
            : key;
    }

    internal string ResolveLanguageID(string languageId, CultureInfo systemCulture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        ArgumentNullException.ThrowIfNull(systemCulture);
        if (!string.Equals(languageId, "system", StringComparison.OrdinalIgnoreCase))
        {
            return GetLanguage(languageId).Info.LanguageID;
        }

        for (var culture = systemCulture; !culture.Equals(CultureInfo.InvariantCulture); culture = culture.Parent)
        {
            if (packs.TryGetValue(culture.Name, out var exact))
            {
                return exact.Info.LanguageID;
            }
        }

        var language = systemCulture.TwoLetterISOLanguageName;
        var preferred = language switch
        {
            "zh" => "zh-CN",
            "en" => DEFAULT_LANGUAGE_ID,
            _ => null
        };
        if (preferred is not null && packs.ContainsKey(preferred))
        {
            return preferred;
        }

        return KnownLanguages.FirstOrDefault(info =>
            CultureInfo.GetCultureInfo(info.LanguageID).TwoLetterISOLanguageName == language)?.LanguageID ?? DEFAULT_LANGUAGE_ID;
    }
}
