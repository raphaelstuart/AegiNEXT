using AegiNext.Rendering.Fonts;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

internal sealed class FontNamePreviewPresenter : Decorator, IDisposable
{
    private const double MAXIMUM_PREVIEW_WIDTH = 320;
    public static readonly StyledProperty<double> fontSizeProperty = TextBlock.FontSizeProperty.AddOwner<FontNamePreviewPresenter>();
    public static readonly StyledProperty<IBrush?> foregroundProperty = TextBlock.ForegroundProperty.AddOwner<FontNamePreviewPresenter>();
    private readonly FontSelection selection;
    private readonly IFontNamePreviewProvider provider;
    private readonly TextBlock fallback = new() { TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false };
    private TopLevel? scalingHost;
    private ScrollViewer? scroller;
    private CancellationTokenSource? cancellation;
    private FontNamePreviewRequest? activeRequest;
    private FontNamePreview? preview;
    private WriteableBitmap? bitmap;
    private ImageBrush? mask;
    private int revision;
    private bool attached;
    private bool disposed;

    static FontNamePreviewPresenter()
    {
        AffectsRender<FontNamePreviewPresenter>(foregroundProperty);
    }

    internal FontNamePreviewPresenter(string name, FontSelection selection, IFontNamePreviewProvider provider)
    {
        this.selection = selection;
        this.provider = provider;
        Text = name;
        fallback.Text = name;
        Child = fallback;
        MaxWidth = MAXIMUM_PREVIEW_WIDTH;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    internal bool HasPreview => bitmap is not null;
    internal string Text { get; }

    public double FontSize
    {
        get => GetValue(fontSizeProperty);
        set => SetValue(fontSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(foregroundProperty);
        set => SetValue(foregroundProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (preview is null || mask is null)
        {
            base.Render(context);
            return;
        }
        var scale = Math.Min(1, Bounds.Width / preview.LogicalWidth);
        var width = preview.LogicalWidth * scale;
        var height = preview.LogicalHeight * scale;
        var rectangle = new Rect(0, Math.Max(0, (Bounds.Height - height) / 2), width, height);
        using (context.PushOpacityMask(mask, rectangle))
        {
            context.DrawRectangle(Foreground, null, rectangle);
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fallbackSize = base.MeasureOverride(availableSize);
        return preview is null ? fallbackSize : new(Math.Min(preview.LogicalWidth, availableSize.Width),
            Math.Max(fallbackSize.Height, preview.LogicalHeight));
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (disposed)
        {
            return;
        }
        attached = true;
        scalingHost = TopLevel.GetTopLevel(this);
        if (scalingHost is not null)
        {
            scalingHost.ScalingChanged += OnScalingChanged;
        }
        scroller = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller is not null)
        {
            scroller.ScrollChanged += OnScrollChanged;
        }
        LayoutUpdated += OnLayoutUpdated;
        RefreshVisibility();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Deactivate();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Deactivate();
    }

    private void Deactivate()
    {
        attached = false;
        LayoutUpdated -= OnLayoutUpdated;
        if (scalingHost is not null)
        {
            scalingHost.ScalingChanged -= OnScalingChanged;
        }
        if (scroller is not null)
        {
            scroller.ScrollChanged -= OnScrollChanged;
        }
        scalingHost = null;
        scroller = null;
        CancelRequest();
        ClearPreview();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == fontSizeProperty || change.Property == MaxWidthProperty)
        {
            CancelRequest();
            ClearPreview();
            RefreshVisibility();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e) => RefreshVisibility();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => RefreshVisibility();

    private void OnScalingChanged(object? sender, EventArgs e) => RefreshVisibility();

    private void RefreshVisibility()
    {
        if (!IsInViewport())
        {
            if (activeRequest is not null)
            {
                CancelRequest();
                ClearPreview();
            }
            return;
        }
        var request = new FontNamePreviewRequest(selection.FamilyName, selection.Variant, Text,
            FontSize, scalingHost?.RenderScaling ?? 1, MaxWidth);
        if (activeRequest == request)
        {
            return;
        }
        CancelRequest();
        ClearPreview();
        activeRequest = request;
        cancellation = new();
        _ = LoadPreviewAsync(request, cancellation, revision);
    }

    private bool IsInViewport()
    {
        if (!attached || !IsEffectivelyVisible || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return false;
        }
        if (scroller is null)
        {
            return true;
        }
        return this.TranslatePoint(default, scroller) is { } point &&
            new Rect(point, Bounds.Size).Intersects(new Rect(scroller.Bounds.Size));
    }

    private async Task LoadPreviewAsync(FontNamePreviewRequest request, CancellationTokenSource requestCancellation, int requestRevision)
    {
        try
        {
            var result = await provider.GetPreviewAsync(request, requestCancellation.Token).ConfigureAwait(false);
            if (result is null)
            {
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestCancellation.IsCancellationRequested || requestRevision != revision || !IsInViewport() || activeRequest != request)
                {
                    return;
                }
                Present(result);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Trace.TraceError("字体名称预览失败：{0}", error);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(cancellation, requestCancellation))
                {
                    cancellation = null;
                }
                requestCancellation.Dispose();
            });
        }
    }

    private unsafe void Present(FontNamePreview result)
    {
        var replacement = new WriteableBitmap(new(result.PixelWidth, result.PixelHeight), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using var target = replacement.Lock();
            for (var row = 0; row < result.PixelHeight; row++)
            {
                var source = result.Alpha.Span.Slice(row * result.PixelWidth, result.PixelWidth);
                var destination = new Span<byte>((void*)(target.Address + row * target.RowBytes), result.PixelWidth * 4);
                for (var column = 0; column < source.Length; column++)
                {
                    destination.Slice(column * 4, 4).Fill(source[column]);
                }
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
        bitmap?.Dispose();
        bitmap = replacement;
        mask = new(replacement) { Stretch = Stretch.Fill };
        preview = result;
        fallback.Opacity = 0;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void CancelRequest()
    {
        ++revision;
        cancellation?.Cancel();
        cancellation = null;
        activeRequest = null;
    }

    private void ClearPreview()
    {
        bitmap?.Dispose();
        bitmap = null;
        mask = null;
        preview = null;
        fallback.Opacity = 1;
        InvalidateMeasure();
        InvalidateVisual();
    }
}
