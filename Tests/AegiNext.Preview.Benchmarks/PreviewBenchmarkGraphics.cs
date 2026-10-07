using System.Runtime.ExceptionServices;
using System.Diagnostics;
using AegiNext.Desktop.Rendering;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace AegiNext.Preview.Benchmarks;

/// <summary>真实桌面图形后端的离屏基准；不创建可见窗口，不替换为 Headless 或软件 GL。</summary>
internal static class PreviewBenchmarkGraphics
{
    internal static void Run(Action<PreviewGraphicsContext, IOpenGlTextureSharingRenderInterfaceContextFeature> benchmark)
    {
        AppBuilder.Configure<PreviewBenchmarkApplication>().UsePlatformDetect().SetupWithoutStarting();
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
        using var cancellation = new CancellationTokenSource();
        Exception? failure = null;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var compositor = Compositor.TryGetDefaultCompositor() ?? throw new NotSupportedException("No compositor.");
                var feature = await compositor.TryGetRenderInterfaceFeature(typeof(IOpenGlTextureSharingRenderInterfaceContextFeature))
                    as IOpenGlTextureSharingRenderInterfaceContextFeature;
                Console.WriteLine($"Preview graphics feature: {feature?.GetType().Name ?? "none"}; shared contexts: {feature?.CanCreateSharedContext}");
                await Task.Run(() =>
                {
                    using var graphics = PreviewGraphicsContext.TryCreate(feature) ?? throw new NotSupportedException("No hardware F16 GPU preview context.");
                    using var current = graphics.MakeCurrent();
                    benchmark(graphics, feature!);
                });
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                cancellation.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(cancellation.Token);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
