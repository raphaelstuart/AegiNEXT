using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Editing;

internal sealed class ExportSettingsValidationException : Exception
{
    internal ExportSettingsValidationException(string fieldKey, string localizationKey)
        : base(Localization.Get(localizationKey))
    {
        FieldKey = fieldKey;
        LocalizationKey = localizationKey;
    }

    internal string FieldKey { get; }
    internal string LocalizationKey { get; }
}
