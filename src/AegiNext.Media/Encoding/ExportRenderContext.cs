using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering;
using AegiNext.Rendering.Projects;

namespace AegiNext.Media.Encoding;

internal sealed class ExportRenderContext : IDisposable
{
    private readonly ProjectSceneRenderer renderer;
    private readonly LinearRenderSurface surface;
    private readonly Half[] pixels;
    private readonly IProgress<VideoExportProgress>? progress;
    private readonly CancellationToken cancellationToken;
    private readonly ProjectDocument project;
    private ulong frames;

    internal ExportRenderContext(ProjectDocument project, string projectDirectory, IProgress<VideoExportProgress>? progress, CancellationToken cancellationToken)
    {
        this.project = project;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        renderer = new(new DirectoryProjectAssetResolver(projectDirectory));
        surface = new(new(project.Width, project.Height, (float)project.ReferenceWhiteNits));
        pixels = new Half[surface.Info.ChannelCount];
    }

    internal Exception? Failure { get; private set; }

    internal unsafe int Render(long pts, int timeBaseNumerator, int timeBaseDenominator, uint width, uint height, float* output, ulong channels)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (width != project.Width || height != project.Height || channels != (ulong)pixels.Length || output is null)
            {
                throw new InvalidDataException("原生导出帧尺寸与工程不一致。");
            }

            var time = new MediaTimestamp(pts, new(timeBaseNumerator, timeBaseDenominator)).ToMediaTime() - project.Media!.MediaOrigin;
            renderer.RenderInto(project, time, surface);
            surface.CopyPixels(pixels);
            var destination = new Span<float>(output, pixels.Length);
            for (var index = 0; index < pixels.Length; index++)
            {
                destination[index] = (float)pixels[index];
            }

            frames++;
            if (frames == 1 || frames % 10 == 0)
            {
                progress?.Report(new(frames, time, null, "encoding"));
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        catch (Exception exception)
        {
            Failure = exception;
            return 2;
        }
    }

    public void Dispose()
    {
        surface.Dispose();
        renderer.Dispose();
    }
}
