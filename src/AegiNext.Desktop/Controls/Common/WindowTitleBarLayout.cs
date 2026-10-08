using Avalonia;
using Avalonia.Controls;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>按交互内容实际宽度分配菜单与标题，保留中央和右侧入口空间。</summary>
public sealed class WindowTitleBarLayout : Panel
{
    private const double CONTENT_SPACING = 12;
    private double availableMenuWidth;

    public event EventHandler? AvailableMenuWidthChanged;
    public double AvailableMenuWidth => availableMenuWidth;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 4)
        {
            return base.MeasureOverride(availableSize);
        }

        var menu = Children[0];
        var center = Children[1];
        var title = Children[2];
        var right = Children[3];
        right.Measure(availableSize);
        var rightWidth = right.DesiredSize.Width;
        var rightSpacing = rightWidth > 0 ? CONTENT_SPACING : 0;
        var contentWidth = Math.Max(0, availableSize.Width - rightWidth - rightSpacing);
        center.Measure(new(contentWidth, availableSize.Height));
        title.Measure(new(double.PositiveInfinity, availableSize.Height));
        var hasCenter = center is ContentControl { Content: not null };
        var menuWidth = hasCenter
            ? Math.Max(0, (contentWidth - center.DesiredSize.Width) / 2 - CONTENT_SPACING)
            : Math.Max(0, contentWidth - Math.Min(title.DesiredSize.Width, contentWidth / 2) - CONTENT_SPACING);
        SetAvailableMenuWidth(menuWidth);
        menu.Measure(new(menuWidth, availableSize.Height));
        var titleWidth = hasCenter ? menuWidth : Math.Max(0, contentWidth - menu.DesiredSize.Width - CONTENT_SPACING);
        title.Measure(new(titleWidth, availableSize.Height));
        return new(menu.DesiredSize.Width + center.DesiredSize.Width + title.DesiredSize.Width + rightWidth +
            rightSpacing + CONTENT_SPACING * (hasCenter ? 2 : 1),
            Math.Max(right.DesiredSize.Height, Math.Max(center.DesiredSize.Height, Math.Max(menu.DesiredSize.Height, title.DesiredSize.Height))));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 4)
        {
            return base.ArrangeOverride(finalSize);
        }

        var menu = Children[0];
        var center = Children[1];
        var title = Children[2];
        var right = Children[3];
        var rightWidth = Math.Min(right.DesiredSize.Width, finalSize.Width);
        var rightSpacing = rightWidth > 0 ? CONTENT_SPACING : 0;
        var contentWidth = Math.Max(0, finalSize.Width - rightWidth - rightSpacing);
        right.Arrange(new(finalSize.Width - rightWidth, 0, rightWidth, finalSize.Height));
        var hasCenter = center is ContentControl { Content: not null };
        if (hasCenter)
        {
            var centerWidth = Math.Min(center.DesiredSize.Width, contentWidth);
            var centerLeft = (contentWidth - centerWidth) / 2;
            var sideWidth = Math.Max(0, centerLeft - CONTENT_SPACING);
            SetAvailableMenuWidth(sideWidth);
            menu.Arrange(new(0, 0, Math.Min(menu.DesiredSize.Width, sideWidth), finalSize.Height));
            center.Arrange(new(centerLeft, 0, centerWidth, finalSize.Height));
            title.Arrange(new(centerLeft + centerWidth + CONTENT_SPACING, 0, sideWidth, finalSize.Height));
        }
        else
        {
            var menuWidth = Math.Min(menu.DesiredSize.Width, contentWidth);
            menu.Arrange(new(0, 0, menuWidth, finalSize.Height));
            center.Arrange(new(0, 0, 0, finalSize.Height));
            title.Arrange(new(menuWidth + CONTENT_SPACING, 0,
                Math.Max(0, contentWidth - menuWidth - CONTENT_SPACING), finalSize.Height));
        }

        return finalSize;
    }

    private void SetAvailableMenuWidth(double value)
    {
        if (availableMenuWidth.Equals(value))
        {
            return;
        }

        availableMenuWidth = value;
        AvailableMenuWidthChanged?.Invoke(this, EventArgs.Empty);
    }
}
