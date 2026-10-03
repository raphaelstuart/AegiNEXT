using System.Runtime.Versioning;
using AegiNext.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AegiNext.Desktop.Controls;

/// <summary>
/// 在 Avalonia 布局中托管原生 HDR 视图；原生资源只在框架解除 attachment 后释放。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class HdrPreviewControl : NativeControlHost
{
    private readonly HdrFrame frame;
    private readonly DispatcherTimer timer;
    private MacHdrPreview? preview;

    /// <summary>
    /// 用不可变测试帧建立预览控件；刷新显示状态，不承担视频播放时钟。
    /// </summary>
    public HdrPreviewControl(HdrFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        this.frame = frame;
        timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += OnTick;
    }

    public HdrPreviewStatus? Status { get; private set; }
    public HdrVerificationResult? Verification { get; private set; }
    public string? Failure { get; private set; }
    public int CreatedContexts { get; private set; }
    public bool HasNativeContext => preview is not null;
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        try
        {
            preview = MacHdrPreview.Create();
            CreatedContexts++;
            Status = null;
            Verification = null;
            Failure = null;
            return new PlatformHandle(preview.View, "NSView");
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Failure = exception.Message;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return base.CreateNativeControlCore(parent);
        }
    }

    /// <inheritdoc />
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        timer.Stop();
        if (preview is not null)
        {
            preview.Dispose();
            preview = null;
        }
        else
        {
            base.DestroyNativeControlCore(control);
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (preview is null || !IsEffectivelyVisible)
        {
            return;
        }

        try
        {
            Status = preview.Present(frame);
            if (Status is not null)
            {
                Verification ??= preview.VerifyGpuPipeline();
                if (!Verification.PipelineOk)
                {
                    throw new InvalidOperationException("GPU 数值验证未通过。");
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            Failure = exception.Message;
            timer.Stop();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
