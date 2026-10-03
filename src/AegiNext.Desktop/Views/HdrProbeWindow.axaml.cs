using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Diagnostics;
using AegiNext.Media;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AegiNext.Desktop.Views;

/// <summary>
/// 原生 HDR 技术诊断窗口，覆盖显示、尺寸变化、重新附着与显式释放。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class HdrProbeWindow : Window
{
    private static readonly JsonSerializerOptions reportOptions = new() { WriteIndented = true };
    private readonly HdrProbeOptions options;
    private readonly HdrProbeReport report;
    private readonly HdrPreviewControl preview;
    private readonly Border previewSlot;
    private readonly TextBlock statusText;
    private Task automaticTask = Task.CompletedTask;
    private bool closing;
    private bool canClose;

    /// <summary>
    /// 为 XAML 装载创建交互式诊断窗口。
    /// </summary>
    public HdrProbeWindow() : this(HdrProbeOptions.Parse(["--hdr-probe"]))
    {
    }

    internal HdrProbeWindow(HdrProbeOptions options)
    {
        this.options = options;
        report = new() { Automatic = options.Automatic };
        AvaloniaXamlLoader.Load(this);
        previewSlot = this.FindControl<Border>("PreviewSlot") ?? throw new InvalidOperationException("缺少预览容器。");
        statusText = this.FindControl<TextBlock>("StatusText") ?? throw new InvalidOperationException("缺少状态文本。");
        var close = this.FindControl<Button>("CloseButton") ?? throw new InvalidOperationException("缺少关闭按钮。");
        close.Click += (_, _) => Close();
        preview = new(HdrProbePattern.Create(options.FontPath));
        preview.StateChanged += OnPreviewStateChanged;
        previewSlot.Child = preview;
        if (options.Automatic)
        {
            Opened += RunAutomaticChecks;
        }
    }

    public int ExitCode { get; private set; }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var beginCleanup = false;
        if (!canClose)
        {
            e.Cancel = true;
            if (!closing)
            {
                closing = true;
                beginCleanup = true;
            }
        }

        base.OnClosing(e);
        if (beginCleanup)
        {
            ReleaseAndClose();
        }
    }

    private void OnPreviewStateChanged(object? sender, EventArgs e)
    {
        if (preview.Failure is not null)
        {
            statusText.Text = preview.Failure;
            report.Failure = preview.Failure;
            return;
        }

        if (preview.Status is not { } status)
        {
            statusText.Text = "等待可呈现的原生视图…";
            return;
        }

        report.Final = status;
        report.Verification = preview.Verification;
        statusText.Text = FormattableString.Invariant(
            $"FP16 · Linear Display P3 · EDR {status.CurrentHeadroom:F2}×   |   {status.DrawableWidth} × {status.DrawableHeight}   |   GPU {(preview.Verification?.PipelineOk == true ? "✓" : "…")}");
    }

    private async void RunAutomaticChecks(object? sender, EventArgs e)
    {
        using var cancellation = new CancellationTokenSource();
        EventHandler<WindowClosingEventArgs> cancelOnClosing = (_, _) => cancellation.Cancel();
        Closing += cancelOnClosing;
        try
        {
            automaticTask = RunAutomaticChecksAsync(cancellation.Token);
            await automaticTask;
        }
        finally
        {
            Closing -= cancelOnClosing;
        }

        if (!closing)
        {
            Close();
        }
    }

    private async Task RunAutomaticChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            await WaitFor(() => preview.Status is { SubmittedFrames: >= 2 } && preview.Verification is not null, cancellationToken: cancellationToken);
            report.Initial = preview.Status;
            Width = 860;
            Height = 610;
            await WaitFor(() => preview.Status is { } state &&
                (state.DrawableWidth != report.Initial!.DrawableWidth || state.DrawableHeight != report.Initial.DrawableHeight), cancellationToken: cancellationToken);
            report.Resized = preview.Status;
            report.ResizeVerified = true;

            var count = preview.CreatedContexts;
            var frames = preview.Status!.SubmittedFrames;
            previewSlot.Child = null;
            previewSlot.Child = preview;
            await WaitFor(() => preview.Status is { } state && state.SubmittedFrames > frames, cancellationToken: cancellationToken);
            report.ReparentVerified = preview.CreatedContexts == count;

            previewSlot.Child = null;
            await WaitFor(() => !preview.HasNativeContext, false, cancellationToken);
            if (MacHdrPreview.GetLiveContextCount() != 0)
            {
                throw new InvalidOperationException("解除附着后仍有原生会话存活。");
            }

            previewSlot.Child = preview;
            await WaitFor(() => preview.CreatedContexts == count + 1 && preview.Status is not null && preview.Verification is not null, cancellationToken: cancellationToken);
            report.RecreationVerified = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report.Failure ??= "自动验证在完成前被关闭。";
        }
        catch (Exception exception)
        {
            report.Failure = exception.Message;
        }
    }

    private async Task WaitFor(Func<bool> condition, bool checkFailure = true, CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        while (!condition())
        {
            if (checkFailure && preview.Failure is not null)
            {
                throw new InvalidOperationException(preview.Failure);
            }

            if (elapsed.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("原生 HDR 验证等待超时。");
            }

            await Task.Delay(50, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async void ReleaseAndClose()
    {
        try
        {
            await automaticTask;
            report.CreatedContexts = preview.CreatedContexts;
            previewSlot.Child = null;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await WaitFor(() => !preview.HasNativeContext, checkFailure: false);
            if (report.CreatedContexts > 0)
            {
                report.LiveContextsAfterClose = MacHdrPreview.GetLiveContextCount();
            }

            if (report.LiveContextsAfterClose is > 0)
            {
                report.Failure = "窗口关闭后仍有原生会话存活。";
            }

            if (options.Automatic && (!report.ResizeVerified || !report.ReparentVerified || !report.RecreationVerified))
            {
                report.Failure ??= "生命周期验证未完成。";
            }

            var directory = Path.GetDirectoryName(options.ReportPath)!;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(options.ReportPath, JsonSerializer.Serialize(report, reportOptions));
        }
        catch (Exception exception)
        {
            report.Failure = exception.Message;
            Console.Error.WriteLine(exception.Message);
        }

        ExitCode = report.Failure is null ? 0 : 1;
        canClose = true;
        Close();
    }
}
