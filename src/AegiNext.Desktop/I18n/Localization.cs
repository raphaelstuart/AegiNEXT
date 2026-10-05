using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Avalonia.Threading;

namespace AegiNext.Desktop.I18n;

/// <summary>统一管理外置语言资源、当前语言和语言变化通知。</summary>
public static class Localization
{
    private static readonly ConcurrentDictionary<string, CompositeFormat> formats = new(StringComparer.Ordinal);
    private static LocalizationSnapshot? snapshot;

    public static event EventHandler? LanguageChanged;
    public static bool IsInitialized => Volatile.Read(ref snapshot) is not null;
    public static IReadOnlyList<LanguageInfo> KnownLanguages => Snapshot.Catalog.KnownLanguages;
    public static IReadOnlyList<LocalizationDiagnostic> Diagnostics => Snapshot.Catalog.Diagnostics;
    public static string SelectedLanguageID => Snapshot.SelectedLanguageID;
    public static string CurrentLanguageID => Snapshot.CurrentLanguageID;

    private static LocalizationSnapshot Snapshot => Volatile.Read(ref snapshot) ??
        throw new InvalidOperationException("Localization.Initialize must be called before using localization.");

    /// <summary>加载指定目录的语言包并应用初始化时捕获的系统语言。</summary>
    public static void Initialize(string directory)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsInitialized)
        {
            throw new InvalidOperationException("Localization is already initialized.");
        }

        var catalog = LocalizationCatalog.Load(directory);
        var systemCulture = CultureInfo.CurrentUICulture;
        var current = catalog.ResolveLanguageID("system", systemCulture);
        Volatile.Write(ref snapshot, new(catalog, systemCulture, "system", current));
        ApplyCulture(current);
    }

    /// <summary>读取当前语言文本，缺项依次回退英文和原始 key。</summary>
    public static string Get(string key)
    {
        var value = Snapshot;
        return value.Catalog.Get(value.CurrentLanguageID, key);
    }

    /// <summary>在 UI 线程选择已安装语言或跟随系统，并立即通知所有消费方。</summary>
    public static void SetLanguage(string lang)
    {
        Dispatcher.UIThread.VerifyAccess();
        var previous = Snapshot;
        var current = previous.Catalog.ResolveLanguageID(lang, previous.SystemCulture);
        var selected = string.Equals(lang, "system", StringComparison.OrdinalIgnoreCase) ? "system" : current;
        if (previous.SelectedLanguageID == selected && previous.CurrentLanguageID == current)
        {
            return;
        }

        Volatile.Write(ref snapshot, previous with { SelectedLanguageID = selected, CurrentLanguageID = current });
        ApplyCulture(current);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>使用当前数值文化格式化本地化文本中的位置参数。</summary>
    public static string Format(string key, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, formats.GetOrAdd(Get(key), CompositeFormat.Parse), arguments);
    }

    internal static IObservable<string> Observe(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new LocalizationTextObservable(() => Get(key));
    }

    internal static IObservable<string> Observe(Func<string> textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);
        return new LocalizationTextObservable(textProvider);
    }

    private static void ApplyCulture(string languageId)
    {
        var culture = CultureInfo.GetCultureInfo(languageId);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
