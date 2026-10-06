using System.Text.RegularExpressions;
using System.Xml.Linq;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Tests;

/// <summary>Validates actual XAML localization references against both built-in packages.</summary>
public sealed class SettingsLocalizationTests
{
    private static readonly Regex localizationKey = new(@"\{Loc\s+Key=([^}\s]+)\}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] dynamicSettingsKeys =
    [
        "System", "Light", "Dark", "Recording", "FontRequired", "NameRequired", "DuplicateName", "SystemFont", "EmbeddedFont", "SystemMenu", "WindowMenu"
    ];

    /// <summary>Every static markup text and dynamic settings label exists in both resources.</summary>
    [Fact]
    public void EveryXamlReferenceCommandAndAlignmentHasBothLanguages()
    {
        var sourceDirectory = Path.Combine(FindRepositoryRoot(), "src", "AegiNext.Desktop");
        var markup = Directory.EnumerateFiles(sourceDirectory, "*.axaml", SearchOption.AllDirectories)
            .Select(XDocument.Load).ToArray();
        var markupKeys = markup.SelectMany(document => document.Descendants().Attributes())
            .SelectMany(attribute => localizationKey.Matches(attribute.Value).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(markupKeys.Length >= 100, "Expected real Loc references; empty extraction must fail the migration check.");
        Assert.Empty(markup.SelectMany(document => document.Descendants().Attributes("Tag")));
        var keys = markupKeys
            .Concat(Enum.GetNames<WorkbenchCommand>().Select(key => "Settings." + key))
            .Concat(Enum.GetNames<ProjectTextAlignment>().Select(key => "Settings." + key))
            .Concat(dynamicSettingsKeys.Select(key => "Settings." + key))
            .Distinct(StringComparer.Ordinal).ToArray();
        var catalog = LocalizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "i18n"));
        foreach (var languageID in new[] { "en-US", "zh-CN" })
        {
            var pack = catalog.GetLanguage(languageID);
            foreach (var key in keys)
            {
                Assert.True(pack.Strings.ContainsKey(key), $"{languageID} is missing {key}.");
                Assert.False(string.IsNullOrWhiteSpace(pack.Strings[key]), $"{languageID}: {key}");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AegiNext.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("AegiNext.sln was not found.");
    }
}
