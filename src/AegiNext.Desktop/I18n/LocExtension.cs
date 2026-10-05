using Avalonia;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.I18n;

/// <summary>为 XAML 文本属性提供跟随当前语言更新的绑定。</summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Localization.Observe(Key).ToBinding();
    }
}
