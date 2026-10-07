using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>通过指针中心缩放和拖动平移查看预览内容，不修改内容本身。</summary>
public sealed class PreviewViewportControl : Border
{
    private const double MINIMUM_ZOOM = 0.25;
    private const double MAXIMUM_ZOOM = 16;
    private readonly MatrixTransform viewTransform = new();
    private IPointer? panPointer;
    private Point panStart;
    private Vector panOffset;

    /// <summary>创建可命中空白区域并裁切内容的预览视口。</summary>
    public PreviewViewportControl()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
    }

    /// <summary>相对于内容默认显示大小的缩放比例。</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>内容在视口坐标内的平移量。</summary>
    public Vector Offset { get; private set; }

    /// <summary>恢复默认显示大小与位置，并结束当前手势。</summary>
    public void ResetView()
    {
        CancelInteraction();
        Zoom = 1;
        Offset = default;
        UpdateTransform();
    }

    /// <summary>释放指针捕获，保留已调整的查看位置。</summary>
    public void CancelInteraction()
    {
        var pointer = panPointer;
        panPointer = null;
        if (ReferenceEquals(pointer?.Captured, this))
        {
            pointer.Capture(null);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if ((e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0 ||
            e.Delta.Y == 0 || !double.IsFinite(e.Delta.Y))
        {
            return;
        }

        CancelInteraction();
        var anchor = e.GetPosition(this);
        var factor = Math.Pow(1.2, Math.Clamp(e.Delta.Y, -64, 64));
        var next = Math.Clamp(Zoom * factor, MINIMUM_ZOOM, MAXIMUM_ZOOM);
        var ratio = next / Zoom;
        Offset = new(anchor.X - (anchor.X - Offset.X) * ratio, anchor.Y - (anchor.Y - Offset.Y) * ratio);
        Zoom = next;
        UpdateTransform();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || panPointer is not null)
        {
            return;
        }

        panPointer = e.Pointer;
        panStart = e.GetPosition(this);
        panOffset = Offset;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(panPointer, e.Pointer))
        {
            return;
        }

        Offset = panOffset + (e.GetPosition(this) - panStart);
        UpdateTransform();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && ReferenceEquals(panPointer, e.Pointer))
        {
            CancelInteraction();
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        panPointer = null;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelInteraction();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty)
        {
            UpdateTransform();
        }
        else if (change.Property == IsVisibleProperty && !IsVisible ||
                 change.Property == IsEnabledProperty && !IsEnabled)
        {
            CancelInteraction();
        }
    }

    private void UpdateTransform()
    {
        if (Child is not { } child)
        {
            return;
        }

        viewTransform.Matrix = Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Offset);
        child.RenderTransformOrigin = new(0, 0, RelativeUnit.Absolute);
        child.RenderTransform = viewTransform;
    }
}
