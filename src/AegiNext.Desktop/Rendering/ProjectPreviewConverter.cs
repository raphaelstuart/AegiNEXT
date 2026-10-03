using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class ProjectPreviewConverter : IVideoPreviewConverter
{
    private readonly SdrVideoConverter converter = new();
    private readonly Func<ProjectPreviewState> getState;
    private readonly Action<Exception?> reportError;
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private AegiNext.Core.Projects.ProjectDocument? failedDocument;

    internal ProjectPreviewConverter(Func<ProjectPreviewState> getState, Action<Exception?>? reportError = null)
    {
        this.getState = getState;
        this.reportError = reportError ?? (static _ => { });
    }

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        var background = converter.Convert(frame, cancellationToken);
        var state = getState();
        var document = state.Document;
        var timestamp = frame.Info.PresentationTimestamp ?? frame.Info.BestEffortTimestamp
            ?? throw new InvalidDataException("视频帧缺少显示时间。");
        var time = timestamp.ToMediaTime() - (document.Media?.MediaOrigin ?? MediaTime.Zero);
        if (ReferenceEquals(document, failedDocument))
        {
            return background;
        }

        reportError(null);
        if (!document.Layers.Any(layer => time >= layer.Start && time < layer.End))
        {
            return background;
        }

        if (renderer is null || directory != state.Directory)
        {
            renderer?.Dispose();
            directory = state.Directory;
            renderer = new(new DirectoryProjectAssetResolver(directory));
        }

        try
        {
            var pixels = renderer.ComposePreview(document, time, background.Pixels.Span,
                background.Width, background.Height, background.Width * 4);
            cancellationToken.ThrowIfCancellationRequested();
            return new(background.Width, background.Height, pixels);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            failedDocument = document;
            reportError(error);
            return background;
        }
    }

    public void Dispose()
    {
        renderer?.Dispose();
        converter.Dispose();
    }
}
