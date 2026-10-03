using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>组合宿主提供的标题与菜单，为原生窗口按钮保留平台测量的空间。</summary>
public sealed partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<WindowTitleBar, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<Control?> MenuContentProperty =
        AvaloniaProperty.Register<WindowTitleBar, Control?>(nameof(MenuContent));
    public static readonly StyledProperty<Thickness> CaptionInsetsProperty =
        AvaloniaProperty.Register<WindowTitleBar, Thickness>(nameof(CaptionInsets));

    /// <summary>创建不持有工程、菜单命令或平台窗口的通用标题栏。</summary>
    public WindowTitleBar()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Control? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public Thickness CaptionInsets
    {
        get => GetValue(CaptionInsetsProperty);
        set => SetValue(CaptionInsetsProperty, value);
    }

    /// <summary>使用客户区坐标区分可拖标题与菜单、原生按钮保留区域。</summary>
    public bool IsDragRegion(Point windowPoint)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !IsEffectivelyVisible ||
            window.TranslatePoint(windowPoint, this) is not { } point ||
            !new Rect(Bounds.Size).Contains(point) || point.X < CaptionInsets.Left ||
            point.X >= Bounds.Width - CaptionInsets.Right)
        {
            return false;
        }

        if (MenuContent is { IsEffectivelyVisible: true } menu &&
            this.TranslatePoint(point, menu) is { } menuPoint && new Rect(menu.Bounds.Size).Contains(menuPoint))
        {
            return false;
        }

        return true;
    }
}
