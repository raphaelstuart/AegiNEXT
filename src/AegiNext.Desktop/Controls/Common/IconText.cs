using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>独立组合按钮图标与文本，图标身份不依赖本地化 key。</summary>
public sealed class IconText : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<IconText, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<string> IconKeyProperty = AvaloniaProperty.Register<IconText, string>(nameof(IconKey), "Settings");
    private readonly MaterialIcon icon = WorkbenchIcon.Create("Settings");

    /// <summary>沿用共享字体、行高和图标布局，不改变宿主的 DataContext。</summary>
    public IconText()
    {
        this.Bind(AutomationProperties.NameProperty, this.GetObservable(TextProperty));
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        var label = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));
        label.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension("WorkbenchBodyFontFamily"));
        label.Bind(TextBlock.LineHeightProperty, new DynamicResourceExtension("WorkbenchInputLineHeight"));
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, label }
        };
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconKeyProperty)
        {
            icon.Kind = WorkbenchIcon.ResolveKind(IconKey);
        }
    }
}
