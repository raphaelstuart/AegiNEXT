using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Tasks;

/// <summary>在主窗口标题栏右侧呈现标准任务弹出层，不拥有共享任务服务。</summary>
public sealed partial class TaskCenterView : UserControl
{
    private readonly Flyout flyout;
    private readonly Border flyoutContent;
    private TopLevel? owner;

    /// <summary>限制名称最多占据一半文本区域，短名称按内容宽度排列。</summary>
    public static FuncValueConverter<double, double> NameWidthConverter { get; } = new(width => Math.Max(0, width / 2));

    /// <summary>加载局部视图，隔离宿主的业务数据上下文。</summary>
    public TaskCenterView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        flyout = (Flyout)this.FindControl<Button>("TasksButton")!.Flyout!;
        flyoutContent = (Border)flyout.Content!;
        flyoutContent.AttachedToVisualTree += (_, _) => UpdateFlyoutWidth();
    }

    /// <summary>关闭属于当前主窗口的弹出层。</summary>
    public void CloseFlyout() => flyout.Hide();

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        owner = TopLevel.GetTopLevel(this);
        if (owner is not null)
        {
            owner.PropertyChanged += OnOwnerSizeChanged;
            UpdateFlyoutWidth();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (owner is not null)
        {
            owner.PropertyChanged -= OnOwnerSizeChanged;
            owner = null;
        }

        CloseFlyout();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnOwnerSizeChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TopLevel.ClientSizeProperty)
        {
            UpdateFlyoutWidth();
        }
    }

    private void UpdateFlyoutWidth()
    {
        if (owner is null || owner.ClientSize.Width <= 0)
        {
            return;
        }

        var screenWidth = owner.Screens?.ScreenFromTopLevel(owner)?.WorkingArea.Width / owner.RenderScaling;
        var inset = (Thickness)this.FindResource("WorkbenchInset")!;
        var maximumWidth = Math.Max(0, Math.Min(owner.ClientSize.Width, screenWidth ?? owner.ClientSize.Width) - inset.Left - inset.Right);
        flyoutContent.MaxWidth = maximumWidth;
        var presenter = flyoutContent.GetVisualAncestors().OfType<FlyoutPresenter>().FirstOrDefault();
        if (presenter is not null)
        {
            presenter.MaxWidth = maximumWidth;
        }
    }
}
