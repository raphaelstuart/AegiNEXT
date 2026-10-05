using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AegiNext.Desktop.Controls;

/// <summary>关闭前请求宿主验证草稿，取消时保持原弹层和输入；不持有业务事务。</summary>
public sealed class DraftPopup : PopupFlyoutBase
{
    /// <summary>弹层显示的输入控件。</summary>
    public Control? Child { get; set; }

    /// <summary>弹层在目标控件中的贴靠范围。</summary>
    public Rect? PlacementRect
    {
        get => Popup.PlacementRect;
        set => Popup.PlacementRect = value;
    }

    /// <summary>宿主销毁或目标失效时关闭弹层，不再次发布验证请求。</summary>
    public void ForceClose()
    {
        HideCore(false);
    }

    /// <inheritdoc />
    protected override Control CreatePresenter()
    {
        return Child ?? throw new InvalidOperationException("Draft popup requires content before opening.");
    }
}
