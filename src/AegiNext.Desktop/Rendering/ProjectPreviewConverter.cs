using AegiNext.Core.Timing;
using AegiNext.Core.Projects;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class ProjectPreviewConverter : IVideoPreviewConverter
{
    private readonly SdrVideoConverter converter = new();
    private SdrVideoConverter? interactiveConverter;
    private readonly Func<ProjectPreviewState> getState;
    private readonly Action<Exception?> reportError;
    private readonly PreviewFrameCatalog? previewFrames;
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private AegiNext.Core.Projects.ProjectDocument? failedDocument;

    internal ProjectPreviewConverter(Func<ProjectPreviewState> getState, Action<Exception?>? reportError = null,
        PreviewFrameCatalog? previewFrames = null)
    {
        this.getState = getState;
        this.reportError = reportError ?? (static _ => { });
        this.previewFrames = previewFrames;
    }

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        var state = getState();
        var activeConverter = state.IsInteractive ? interactiveConverter ??= new(new(960, 540)) : converter;
        var background = activeConverter.Convert(frame, cancellationToken);
        var document = state.Document;
        var timestamp = frame.Info.PresentationTimestamp ?? frame.Info.BestEffortTimestamp
            ?? throw new InvalidDataException("视频帧缺少显示时间。");
        var time = state.IsInteractive && state.TargetTime is { } target ? target : timestamp.ToMediaTime() - (document.Media?.MediaOrigin ?? MediaTime.Zero);
        if (ReferenceEquals(document, failedDocument))
        {
            return CompleteFrame(background, background, state, time);
        }

        reportError(null);
        if (renderer is null || directory != state.Directory)
        {
            renderer?.Dispose();
            directory = state.Directory;
            renderer = new(new DirectoryProjectAssetResolver(directory));
        }

        try
        {
            var size = GetPreviewSize(document, background.Width, background.Height);
            var pixels = renderer.ComposePreview(document, time, background.Pixels.Span,
                background.Width, background.Height, background.Width * 4, size.Width, size.Height, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return CompleteFrame(new(size.Width, size.Height, pixels), background, state, time);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            failedDocument = document;
            reportError(error);
            return CompleteFrame(background, background, state, time);
        }
    }

    internal static (int Width, int Height) GetPreviewSize(ProjectDocument document, int width, int height)
    {
        var scale = Math.Min((double)width / document.Width, (double)height / document.Height);
        return (Math.Max(1, (int)Math.Round(document.Width * scale)),
            Math.Max(1, (int)Math.Round(document.Height * scale)));
    }

    private SdrVideoFrame CompleteFrame(SdrVideoFrame presented, SdrVideoFrame background, ProjectPreviewState state, MediaTime time)
    {
        previewFrames?.Register(presented, background, ReferenceEquals(state.Document, failedDocument) ? null : state.Document, time, state.IsInteractive);
        return presented;
    }

    public void Dispose()
    {
        renderer?.Dispose();
        converter.Dispose();
        interactiveConverter?.Dispose();
    }
}
