using System.Globalization;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests;

/// <summary>Verifies external language catalog loading, discovery and fallback behavior.</summary>
public sealed class LocalizationCatalogTests
{
    /// <summary>Language metadata controls discovery independently of file names.</summary>
    [Fact]
    public void DiscoversThirdLanguageFromMetadataAndOnlyScansDirectoryRoot()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("ja-JP", "日本語", fileName: "custom.json");
        var nestedDirectory = Path.Combine(directory.Path, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "fr-FR.json"), """
            {"LanguageName":"Français","LanguageID":"fr-FR","Strings":{"Greeting":"Bonjour"}}
            """);
        File.WriteAllText(Path.Combine(directory.Path, "de-DE.txt"), """
            {"LanguageName":"Deutsch","LanguageID":"de-DE","Strings":{"Greeting":"Hallo"}}
            """);

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal(2, catalog.KnownLanguages.Count);
        Assert.Contains(catalog.KnownLanguages, language => language.LanguageID == "ja-JP" && language.LanguageName == "日本語");
        Assert.Equal("日本語", catalog.Get("ja-JP", "Greeting"));
        Assert.Empty(catalog.Diagnostics);
    }

    /// <summary>Identifiers ignore case while translation keys retain ordinal identity.</summary>
    [Fact]
    public void NormalizesLanguageIdentifiersButPreservesCaseSensitiveKeys()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("ZH-cn", "简体中文", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Greeting"] = "你好",
            ["greeting"] = "小写"
        });

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Contains(catalog.KnownLanguages, language => language.LanguageID == "zh-CN");
        Assert.Equal("zh-CN", catalog.ResolveLanguageID("zH-cN", CultureInfo.InvariantCulture));
        Assert.Equal("你好", catalog.Get("ZH-CN", "Greeting"));
        Assert.Equal("小写", catalog.Get("zh-cn", "greeting"));
        Assert.Equal("GREETING", catalog.Get("zh-CN", "GREETING"));
    }

    /// <summary>Missing translations fall back to English and then the unchanged key.</summary>
    [Fact]
    public void ResolvesCurrentLanguageThenDefaultLanguageThenKey()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("zh-CN", "简体中文");

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal("简体中文", catalog.Get("zh-CN", "Greeting"));
        Assert.Equal("Default language", catalog.Get("zh-CN", "Fallback"));
        Assert.Equal("Missing.Key", catalog.Get("zh-CN", "Missing.Key"));
    }

    /// <summary>A loaded catalog retains its snapshot until it is explicitly reloaded.</summary>
    [Fact]
    public void CatalogSnapshotDoesNotChangeWhenLanguageFileChanges()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("ja-JP", "日本語");
        var catalog = LocalizationCatalog.Load(directory.Path);

        directory.WriteLanguage("ja-JP", "変更");
        directory.WriteLanguage("fr-FR", "Français");

        Assert.Equal("日本語", catalog.Get("ja-JP", "Greeting"));
        Assert.DoesNotContain(catalog.KnownLanguages, language => language.LanguageID == "fr-FR");
        Assert.Equal("変更", LocalizationCatalog.Load(directory.Path).Get("ja-JP", "Greeting"));
    }

    /// <summary>Damaged or invalid optional packages are excluded with a file diagnostic.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\"}")]
    [InlineData("{\"LanguageName\":\"  \",\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"!!!\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":12,\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":12,\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":[]}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":12}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":null}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":{}}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageName\":\"重複\",\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{},\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":\"一\",\"Greeting\":\"二\"}}")]
    public void ExcludesInvalidOptionalPackagesWithDiagnostic(string json)
    {
        using var directory = new TemporaryLocalizationDirectory();
        var damagedPath = directory.WriteRaw("optional.json", json);

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Single(catalog.KnownLanguages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(damagedPath, diagnostic.FilePath);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.Equal("Hello", catalog.Get("en-US", "Greeting"));
    }

    /// <summary>Duplicate identifiers exclude every conflicting optional package.</summary>
    [Fact]
    public void DuplicateLanguageIdentifiersExcludeEntireConflictGroup()
    {
        using var directory = new TemporaryLocalizationDirectory();
        var firstPath = directory.WriteLanguage("ja-JP", "日本語", fileName: "first.json");
        var secondPath = directory.WriteLanguage("JA-jp", "Japanese", fileName: "second.json");
        directory.WriteLanguage("fr-FR", "Français");

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal(2, catalog.KnownLanguages.Count);
        Assert.DoesNotContain(catalog.KnownLanguages, language => language.LanguageID == "ja-JP");
        Assert.Equal(2, catalog.Diagnostics.Count);
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.FilePath == firstPath);
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.FilePath == secondPath);
        Assert.Throws<ArgumentException>(() => catalog.ResolveLanguageID("ja-JP", CultureInfo.InvariantCulture));
    }

    /// <summary>The English default package must be installed.</summary>
    [Fact]
    public void MissingDefaultLanguageFailsCatalogLoad()
    {
        using var directory = new TemporaryLocalizationDirectory(includeDefaultLanguage: false);
        directory.WriteLanguage("zh-CN", "简体中文");

        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(directory.Path));
    }

    /// <summary>An invalid default package cannot be silently replaced by an optional language.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("{\"LanguageName\":\"  \",\"LanguageID\":\"en-US\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"English\",\"LanguageID\":\"en-US\",\"Strings\":{\"Greeting\":false}}")]
    [InlineData("{\"LanguageName\":\"English\",\"LanguageID\":\"en-US\",\"Strings\":{\"Greeting\":\"One\",\"Greeting\":\"Two\"}}")]
    public void InvalidDefaultLanguageFailsCatalogLoad(string json)
    {
        using var directory = new TemporaryLocalizationDirectory(includeDefaultLanguage: false);
        directory.WriteRaw("en-US.json", json);
        directory.WriteLanguage("zh-CN", "简体中文");

        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(directory.Path));
    }

    /// <summary>Duplicate default identifiers fail even if their filenames are different.</summary>
    [Fact]
    public void DuplicateDefaultLanguageFailsCatalogLoad()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("EN-us", "Duplicate English", fileName: "duplicate.json");

        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(directory.Path));
    }

    /// <summary>Direct unknown and empty language identifiers are rejected.</summary>
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsUnknownExplicitLanguage(string languageID)
    {
        using var directory = new TemporaryLocalizationDirectory();
        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.ThrowsAny<ArgumentException>(() => catalog.ResolveLanguageID(languageID, CultureInfo.InvariantCulture));
        Assert.ThrowsAny<ArgumentException>(() => catalog.Get(languageID, "Greeting"));
        Assert.Equal("Hello", catalog.Get("en-US", "Greeting"));
    }

    /// <summary>Empty keys cannot be used for text lookup.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsEmptyTextKey(string key)
    {
        using var directory = new TemporaryLocalizationDirectory();
        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.ThrowsAny<ArgumentException>(() => catalog.Get("en-US", key));
    }

    /// <summary>System resolution prefers exact and parent packages before same-language candidates.</summary>
    [Theory]
    [InlineData("fr-CA", "fr-CA")]
    [InlineData("fr-CH", "fr")]
    [InlineData("en-GB", "en-US")]
    [InlineData("zh-HK", "zh-CN")]
    [InlineData("es-AR", "es-ES")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", "en-US")]
    public void ResolvesSystemLanguageWithDeterministicFallback(string systemLanguageID, string expectedLanguageID)
    {
        using var directory = new TemporaryLocalizationDirectory();
        foreach (var languageID in new[] { "fr-CA", "fr-FR", "fr", "en-AU", "zh-CN", "zh-TW", "es-MX", "es-ES" })
        {
            directory.WriteLanguage(languageID, languageID);
        }
        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal(expectedLanguageID, catalog.ResolveLanguageID("system", CultureInfo.GetCultureInfo(systemLanguageID)));
    }

    /// <summary>A regional parent package outranks preferred Chinese same-language candidates.</summary>
    [Fact]
    public void SystemLanguageUsesScriptParentBeforeChinesePreferredPackage()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("zh-CN", "简体中文");
        directory.WriteLanguage("zh-Hant", "繁體中文");
        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal("zh-Hant", catalog.ResolveLanguageID("system", CultureInfo.GetCultureInfo("zh-Hant-HK")));
    }

    /// <summary>Text lookup depends on the requested language rather than the caller's ambient culture.</summary>
    [Fact]
    public async Task BackgroundLookupDoesNotDependOnThreadUiCulture()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("zh-CN", "简体中文");
        var catalog = LocalizationCatalog.Load(directory.Path);

        var result = await Task.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
                return (English: catalog.Get("en-US", "Greeting"), Chinese: catalog.Get("zh-CN", "Greeting"));
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        });

        Assert.Equal("Hello", result.English);
        Assert.Equal("简体中文", result.Chinese);
    }
}
