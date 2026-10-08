using System.Diagnostics;
using System.Numerics;
using AegiNext.Rendering;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.OpenGL.Egl;
using SkiaSharp;

namespace AegiNext.Desktop.Rendering;

/// <summary>仅供串行预览工作线程使用的独立 GL/Skia 上下文；不借用界面线程的当前上下文。</summary>
internal sealed class PreviewGraphicsContext : IDisposable
{
    private const long RESOURCE_CACHE_BYTES = 128L * 1024 * 1024;
    private static readonly string[] softwareRenderers = ["Microsoft Basic Render Driver", "llvmpipe", "softpipe", "SwiftShader", "Software Rasterizer", "GDI Generic"];
    private readonly IGlContext platformContext;
    private readonly IDisposable? platformOwner;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool disposed;

    private PreviewGraphicsContext(IGlContext platformContext, GRContext context, IDisposable? platformOwner)
    {
        this.platformContext = platformContext;
        this.platformOwner = platformOwner;
        Context = context;
    }

    internal GRContext Context { get; }
    internal string? Description => platformContext.GlInterface.Renderer;

    internal static PreviewGraphicsContext? TryCreate(IOpenGlTextureSharingRenderInterfaceContextFeature? graphics)
    {
        if (graphics is null || (!graphics.CanCreateSharedContext && !OperatingSystem.IsWindows()))
        {
            return null;
        }

        IPlatformGraphicsContext? platform = null;
        GRContext? context = null;
        EglDisplay? ownedDisplay = null;
        try
        {
            if (graphics.CanCreateSharedContext)
            {
                platform = graphics.CreateSharedContext();
            }
            else if (OperatingSystem.IsWindows())
            {
                ownedDisplay = WindowsPreviewGraphics.CreateDisplay();
                platform = ownedDisplay.CreateContext(null);
            }
            if (platform is not IGlContext gl)
            {
                return null;
            }
            using var current = gl.MakeCurrent();
            var description = gl.GlInterface.Renderer ?? string.Empty;
            if (softwareRenderers.Any(value => description.Contains(value, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            using var api = gl.Version.Type == GlProfileType.OpenGL
                ? GRGlInterface.CreateOpenGl(gl.GlInterface.GetProcAddress)
                : GRGlInterface.CreateGles(gl.GlInterface.GetProcAddress);
            context = GRContext.CreateGl(api) ?? throw new InvalidOperationException("无法创建预览 GPU 绘制上下文。");
            context.SetResourceCacheLimit(RESOURCE_CACHE_BYTES);
            using var probe = new LinearRenderSurface(new(1, 1, 203), context);
            probe.FillRectangle(Vector2.Zero, Vector2.One, new(4, 2, 0, 0.25f));
            Span<Half> values = stackalloc Half[4];
            probe.CopyPixels(values);
            if (Math.Abs((float)values[0] - 1) > 0.004f || Math.Abs((float)values[1] - 0.5f) > 0.004f ||
                Math.Abs((float)values[3] - 0.25f) > 0.004f || probe.CopySrgbBgra().Length != 4)
            {
                throw new NotSupportedException("预览 GPU 未能保留线性 F16 预乘颜色。");
            }
            var result = new PreviewGraphicsContext(gl, context, ownedDisplay);
            platform = null;
            context = null;
            ownedDisplay = null;
            return result;
        }
        catch (Exception error) when (error is OpenGlException or PlatformGraphicsContextLostException or NotSupportedException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning("GPU project preview initialization failed; CPU preview remains available: {0}", error.Message);
            return null;
        }
        finally
        {
            if (platform is IGlContext gl && context is not null && !context.IsAbandoned)
            {
                try
                {
                    using var current = gl.MakeCurrent();
                    context.Dispose();
                }
                catch (Exception error) when (error is OpenGlException or PlatformGraphicsContextLostException)
                {
                    context.AbandonContext();
                    context.Dispose();
                }
            }
            else
            {
                context?.AbandonContext();
                context?.Dispose();
            }
            platform?.Dispose();
            ownedDisplay?.Dispose();
        }
    }

    internal IDisposable MakeCurrent()
    {
        CheckOwner();
        try
        {
            return platformContext.MakeCurrent();
        }
        catch (Exception error) when (error is OpenGlException or PlatformGraphicsContextLostException)
        {
            Context.AbandonContext();
            throw;
        }
    }

    internal IDisposable? MakeCurrentForDisposal()
    {
        CheckOwner();
        if (Context.IsAbandoned)
        {
            return null;
        }
        try
        {
            return platformContext.MakeCurrent();
        }
        catch (Exception error) when (error is OpenGlException or PlatformGraphicsContextLostException)
        {
            Context.AbandonContext();
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        CheckOwner();
        try
        {
            using (MakeCurrentForDisposal())
            {
                Context.Dispose();
            }
        }
        finally
        {
            try
            {
                platformContext.Dispose();
            }
            finally
            {
                platformOwner?.Dispose();
                disposed = true;
            }
        }
    }

    private void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
        {
            throw new InvalidOperationException("预览 GPU 上下文必须由创建它的工作线程使用和释放。");
        }
    }
}
