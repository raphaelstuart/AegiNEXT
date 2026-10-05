using System.Globalization;

namespace AegiNext.Desktop.I18n;

internal sealed record LocalizationSnapshot(
    LocalizationCatalog Catalog,
    CultureInfo SystemCulture,
    string SelectedLanguageID,
    string CurrentLanguageID);
