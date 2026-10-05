using System.Collections.Frozen;

namespace AegiNext.Desktop.I18n;

internal sealed record LanguagePack(LanguageInfo Info, FrozenDictionary<string, string> Strings);
