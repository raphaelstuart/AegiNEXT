using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace AegiNext.Desktop.I18n;

internal static class LanguagePackReader
{
    internal static LanguagePack Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A language pack must be a JSON object.");
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.Add(property.Name))
            {
                throw new InvalidDataException($"Duplicate language pack field: {property.Name}.");
            }
        }

        var languageName = ReadRequiredString(root, "LanguageName");
        var languageId = ReadRequiredString(root, "LanguageID");
        if (string.Equals(languageId, "system", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The language identifier 'system' is reserved.");
        }

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(languageId);
        }
        catch (CultureNotFoundException error)
        {
            throw new InvalidDataException($"Invalid language identifier: {languageId}.", error);
        }

        if (culture.Equals(CultureInfo.InvariantCulture))
        {
            throw new InvalidDataException("A language identifier must identify a named culture.");
        }

        if (!root.TryGetProperty("Strings", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A language pack must contain a Strings object.");
        }

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in values.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Translation keys must be nonempty and their values must be strings.");
            }

            if (!strings.TryAdd(property.Name, property.Value.GetString()!))
            {
                throw new InvalidDataException($"Duplicate translation key: {property.Name}.");
            }
        }

        return new(new(languageName, culture.Name), strings.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static string ReadRequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"A language pack must contain a nonempty {name} string.");
        }

        return value.GetString()!;
    }
}
