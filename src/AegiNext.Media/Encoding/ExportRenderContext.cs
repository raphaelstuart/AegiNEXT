using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using System.Runtime.InteropServices;

namespace AegiNext.Media.Encoding;

internal sealed class ExportRenderContext : IDisposable
{
    private readonly ProjectSceneRenderer renderer;
    private readonly int channelCount;
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
        channelCount = checked(project.Width * project.Height * 4);
    }

    internal Exception? Failure { get; private set; }
    internal nint NativeContext { get; set; }

    internal unsafe int Render(long pts, int timeBaseNumerator, int timeBaseDenominator, uint width, uint height, float* output, ulong channels)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (width != project.Width || height != project.Height || channels != (ulong)channelCount || output is null)
            {
                throw new InvalidDataException("原生导出帧尺寸与项目不一致。");
            }

            var time = new MediaTimestamp(pts, new(timeBaseNumerator, timeBaseDenominator)).ToMediaTime() - project.Media!.MediaOrigin;
            renderer.CopyCachedFramePixels(project, time, new Span<float>(output, channelCount), cancellationToken);

            frames++;
            if (frames == 1 || frames % 10 == 0)
            {
                var encoder = Marshal.PtrToStringUTF8(NativeExportMethods.EncoderName(NativeContext));
                progress?.Report(new(frames, time, null, "encoding", encoder));
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
        renderer.Dispose();
    }
}
