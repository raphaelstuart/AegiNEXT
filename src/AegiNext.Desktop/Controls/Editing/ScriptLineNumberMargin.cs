using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>复用原生文本布局的固定行号栏，按当前视口绘制并同步垂直滚动。</summary>
public sealed class ScriptLineNumberMargin : Control
{
    private const double HORIZONTAL_PADDING = 6;
    private TextPresenter? presenter;
    private ScrollViewer? scroll;
    private TextLayout? sourceLayout;
    private double[] lineTops = [];
    private double desiredWidth;
    private bool subscribed;

    internal void Connect(TextPresenter? textPresenter, ScrollViewer? scrollViewer)
    {
        Unsubscribe();
        presenter = textPresenter;
        scroll = scrollViewer;
        sourceLayout = null;
        lineTops = [];
        if (this.IsAttachedToVisualTree())
        {
            Subscribe();
        }

        RefreshLayout();
    }

    /// <summary>只格式化当前可见行的号码，使用对应原生行的基线。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (presenter is null || scroll is null || !presenter.IsAttachedToVisualTree())
        {
            return;
        }

        UpdateLayoutData();
        var origin = presenter.TranslatePoint(default, this);
        if (origin is null || sourceLayout is null || lineTops.Length == 0)
        {
            return;
        }

        var viewportTop = origin.Value.Y + scroll.Offset.Y;
        var viewport = new Rect(0, viewportTop, Bounds.Width, scroll.Viewport.Height).Intersect(new Rect(Bounds.Size));
        if (viewport.Height <= 0)
        {
            return;
        }

        var first = Array.BinarySearch(lineTops, viewport.Top - origin.Value.Y);
        first = first < 0 ? Math.Max(0, ~first - 1) : first;
        using var clip = context.PushClip(viewport);
        for (var index = first; index < sourceLayout.TextLines.Count; index++)
        {
            var top = origin.Value.Y + lineTops[index];
            if (top >= viewport.Bottom)
            {
                break;
            }

            var line = sourceLayout.TextLines[index];
            using var number = CreateNumberLayout(index + 1, line.Height);
            var y = top + line.Baseline - number.TextLines[0].Baseline;
            number.Draw(context, new(Bounds.Width - HORIZONTAL_PADDING - number.WidthIncludingTrailingWhitespace, y));
        }
    }

    /// <summary>按当前原生布局的总行数预留完整数字宽度。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateLayoutData();
        return new(desiredWidth, 0);
    }

    /// <summary>恢复对当前模板部件的布局和滚动订阅。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
        RefreshLayout();
    }

    /// <summary>模板重建或宿主移除时释放订阅和原生布局引用。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        sourceLayout = null;
        lineTops = [];
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe()
    {
        if (subscribed || presenter is null || scroll is null)
        {
            return;
        }

        presenter.PropertyChanged += PresenterPropertyChanged;
        presenter.LayoutUpdated += PresenterLayoutUpdated;
        scroll.PropertyChanged += ScrollPropertyChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || presenter is null || scroll is null)
        {
            return;
        }

        presenter.PropertyChanged -= PresenterPropertyChanged;
        presenter.LayoutUpdated -= PresenterLayoutUpdated;
        scroll.PropertyChanged -= ScrollPropertyChanged;
        subscribed = false;
    }

    private void PresenterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != BoundsProperty)
        {
            RefreshLayout();
        }
    }

    private void PresenterLayoutUpdated(object? sender, EventArgs e)
    {
        RefreshLayout();
    }

    private void ScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty || e.Property == ScrollViewer.ViewportProperty)
        {
            InvalidateVisual();
        }
    }

    private void RefreshLayout()
    {
        var previousWidth = desiredWidth;
        UpdateLayoutData();
        if (previousWidth != desiredWidth)
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private void UpdateLayoutData()
    {
        if (presenter is null)
        {
            desiredWidth = 0;
            return;
        }

        var layout = presenter.TextLayout;
        if (ReferenceEquals(layout, sourceLayout))
        {
            return;
        }

        sourceLayout = layout;
        lineTops = new double[layout.TextLines.Count + 1];
        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            lineTops[index + 1] = lineTops[index] + layout.TextLines[index].Height;
        }

        using var widestNumber = CreateNumberLayout(Math.Max(1, layout.TextLines.Count), presenter.LineHeight);
        desiredWidth = Math.Ceiling(widestNumber.WidthIncludingTrailingWhitespace) + HORIZONTAL_PADDING * 2;
    }

    private TextLayout CreateNumberLayout(int number, double lineHeight)
    {
        var typeface = new Typeface(presenter!.FontFamily, presenter.FontStyle, presenter.FontWeight, presenter.FontStretch);
        return new(number.ToString(CultureInfo.InvariantCulture), typeface, presenter.FontSize, presenter.Foreground,
            lineHeight: lineHeight, letterSpacing: presenter.LetterSpacing, fontFeatures: presenter.FontFeatures);
    }
}
