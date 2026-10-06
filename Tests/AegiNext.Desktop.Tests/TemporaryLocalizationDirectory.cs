using System.Text.Json;

namespace AegiNext.Desktop.Tests;

internal sealed class TemporaryLocalizationDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AegiNext-localization-tests-" + Guid.NewGuid().ToString("N"));

    internal TemporaryLocalizationDirectory(bool includeDefaultLanguage = true)
    {
        Directory.CreateDirectory(Path);
        if (includeDefaultLanguage)
        {
            WriteLanguage("en-US", "English", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Greeting"] = "Hello",
                ["Fallback"] = "Default language",
                ["CaseSensitive"] = "Exact key",
                ["Formatted"] = "Value: {0:N1}"
            });
        }
    }

    internal string WriteLanguage(string languageID, string languageName, IReadOnlyDictionary<string, string>? strings = null, string? fileName = null)
    {
        return WriteRaw(fileName ?? languageID + ".json", JsonSerializer.Serialize(new
        {
            LanguageName = languageName,
            LanguageID = languageID,
            Strings = strings ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Greeting"] = languageName
            }
        }));
    }

    internal string WriteRaw(string fileName, string json)
    {
        var filePath = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(filePath, json);
        return filePath;
    }

    /// <summary>Removes the isolated language resource directory.</summary>
    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
