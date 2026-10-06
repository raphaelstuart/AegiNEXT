using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests;

/// <summary>Verifies external language catalogs and the migrated built-in resources.</summary>
public sealed class LocalizationCatalogTests
{
    private static readonly Dictionary<(string LanguageId, string Key), (string Original, string Current)> legacyRenames = new()
    {
        [("en-US", "Workbench.Karaoke")] = ("Karaoke", "Highlight"),
        [("en-US", "Workbench.ClearKaraoke")] = ("Clear karaoke", "Clear highlight"),
        [("en-US", "Workbench.DeleteTrack")] = ("Delete empty track", "Delete track"),
        [("en-US", "Settings.IMPORT_SUBTITLES")] = ("Import subtitles", "Import SRT subtitles"),
        [("en-US", "Settings.EXPORT_SUBTITLES")] = ("Export subtitles", "Export SRT subtitles"),
        [("zh-CN", "Workbench.Karaoke")] = ("逐字高亮", "高亮"),
        [("zh-CN", "Workbench.DeleteTrack")] = ("删除空轨道", "删除轨道"),
        [("zh-CN", "Settings.IMPORT_SUBTITLES")] = ("导入字幕", "导入 SRT 字幕"),
        [("zh-CN", "Settings.EXPORT_SUBTITLES")] = ("导出字幕", "导出 SRT 字幕")
    };
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

    /// <summary>Both built-in packages are copied into the executable resource directory.</summary>
    [Fact]
    public void BuiltInPackagesAreCopiedToOutputAndHaveMatchingCompleteKeys()
    {
        var resourceDirectory = Path.Combine(AppContext.BaseDirectory, "i18n");
        var catalog = LocalizationCatalog.Load(resourceDirectory);
        var english = ReadBuiltInStrings("en-US");
        var chinese = ReadBuiltInStrings("zh-CN");

        Assert.Empty(catalog.Diagnostics);
        Assert.Contains(catalog.KnownLanguages, language => language.LanguageID == "en-US" && !string.IsNullOrWhiteSpace(language.LanguageName));
        Assert.Contains(catalog.KnownLanguages, language => language.LanguageID == "zh-CN" && !string.IsNullOrWhiteSpace(language.LanguageName));
        Assert.True(english.Count >= 466);
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), chinese.Keys.Order(StringComparer.Ordinal));
        foreach (var key in english.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(english[key]), key);
            Assert.False(string.IsNullOrWhiteSpace(chinese[key]), key);
            Assert.Equal(english[key], catalog.Get("en-US", key));
            Assert.Equal(chinese[key], catalog.Get("zh-CN", key));
            var englishFormat = CompositeFormat.Parse(english[key]);
            var chineseFormat = CompositeFormat.Parse(chinese[key]);
            Assert.Equal(englishFormat.MinimumArgumentCount, chineseFormat.MinimumArgumentCount);
        }
    }

    /// <summary>保留原迁移键集和文本，单独验证已更名的界面项，允许后续功能新增文案。</summary>
    [Theory]
    [InlineData("Workbench", "zh-CN", 213, "683239F719DDC91550BC8446EAF44E52BCB2BC37DD15DF65E530D0A2EB4DD1CF")]
    [InlineData("Workbench", "en-US", 213, "25E4FEE652647F2DAA17822D0088D6548EA388B5F46C14F41866670C31FD2EA8")]
    [InlineData("Settings", "zh-CN", 165, "9F42A8A5158D99B3C2660654510FCEE6D89540173C055F68BE2C9CC8846DFDF0")]
    [InlineData("Settings", "en-US", 165, "3F897021081724183B393226777A8562216E8DE993D3BFD00EA99DCADDB05ECB")]
    [InlineData("Preview", "zh-CN", 27, "CA515020C2564EB877AF1B05FBD0C7C7EB70C39523F2EB5136AD6AC4E48F0CF8")]
    [InlineData("Preview", "en-US", 27, "86B76A1117D11F962257E18A0BF34ACFFF8BFF472F44E3F9052B7075AD2501C5")]
    [InlineData("Layout", "zh-CN", 27, "58413CD7E1A199CC54EDA80C0D5ECA33EE334A346896B4F472764FBDE2ECED4C")]
    [InlineData("Layout", "en-US", 27, "14E7D4808E2BD54EC812EA2AB2CBDBD165BA467C03575D41367199BE9B8A3B1F")]
    [InlineData("Log", "zh-CN", 9, "7B7111B256B969F1772C512ABA32506E050EFD500E3BD8D4F4E8D75EF899A82E")]
    [InlineData("Log", "en-US", 9, "C37CDD1984C76719D20BBB3EE6A1B78A592E76BBF291BE2BB87CED8318633C6E")]
    [InlineData("WorkflowLog", "zh-CN", 13, "B98B5EB6A7BE1CD4F89117DD5A7DEBCE311622FD57318E1491A6F75C2E7D0BCC")]
    [InlineData("WorkflowLog", "en-US", 13, "24720BEC3A6A814C717497FD1C7B13D612001340C0AC07477F49537C815FC494")]
    [InlineData("WindowChromeProbe", "zh-CN", 12, "FFCD83CF7326F429C181B4DD2C429AA82F569E6A5A0468E9E0DE00607E431E3A")]
    [InlineData("WindowChromeProbe", "en-US", 12, "80AA301AEC56FC6AA0F2670BDED7E154A6810527B500E6A7FA897A0F1EB3C876")]
    public void MigratedResourcesRetainLegacyText(string prefix, string languageID, int expectedCount, string expectedHash)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacyLocalizationKeys.json")));
        var current = ReadBuiltInStrings(languageID);
        var entries = fixture.RootElement.GetProperty("Keys").EnumerateArray()
            .Select(entry => entry.GetString()!)
            .Where(key => key.StartsWith(prefix + ".", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(key => new KeyValuePair<string, string>(key, RetainLegacyText(languageID, key, current[key])))
            .ToArray();
        var text = string.Join("\n", entries.Select(entry => entry.Key + "\0" + entry.Value));

        Assert.Equal(expectedCount, entries.Length);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    private static string RetainLegacyText(string languageID, string key, string current)
    {
        if (legacyRenames.TryGetValue((languageID, key), out var rename))
        {
            Assert.Equal(rename.Current, current);
            return rename.Original;
        }
        return current;
    }

    private static Dictionary<string, string> ReadBuiltInStrings(string languageID)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", languageID + ".json")));
        return document.RootElement.GetProperty("Strings").EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!, StringComparer.Ordinal);
    }
}
