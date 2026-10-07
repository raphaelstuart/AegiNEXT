using AegiNext.Core.Timing;
using AegiNext.Core.Projects;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;
using AegiNext.Rendering.Fonts;
using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Rendering;

internal sealed class ProjectPreviewConverter : IVideoPreviewConverter
{
    private readonly Dictionary<PreviewQuality, SdrVideoConverter> converters = [];
    private readonly Func<ProjectPreviewState> getState;
    private readonly Action<Exception?> reportError;
    private readonly PreviewFrameCatalog? previewFrames;
    private readonly Func<SystemFontCatalog?>? fontCatalog;
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private AegiNext.Core.Projects.ProjectDocument? failedDocument;
    private bool disposed;

    internal ProjectPreviewConverter(Func<ProjectPreviewState> getState, Action<Exception?>? reportError = null,
        PreviewFrameCatalog? previewFrames = null, Func<SystemFontCatalog?>? fontCatalog = null)
    {
        this.getState = getState;
        this.reportError = reportError ?? (static _ => { });
        this.previewFrames = previewFrames;
        this.fontCatalog = fontCatalog;
    }

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var state = getState();
        var quality = PreviewQualityOptions.GetEffectiveQuality(state.Quality, state.IsInteractive);
        if (!converters.TryGetValue(quality, out var activeConverter))
        {
            activeConverter = new(PreviewQualityOptions.Get(quality));
            converters.Add(quality, activeConverter);
        }
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
            renderer = new(new DirectoryProjectAssetResolver(directory), fontCatalog?.Invoke());
        }

        try
        {
            var size = GetPreviewSize(document, background.Width, background.Height);
            if (size.Width == background.Width && size.Height == background.Height && !renderer.HasPreviewLayers(document, time))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CompleteFrame(background, background, state, time);
            }
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

    /// <inheritdoc />
    public long GetRetainedBytes(SdrVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var background = previewFrames?.FindBackground(frame);
        return frame.Pixels.Length + (background is not null && !ReferenceEquals(background, frame) ? (long)background.Pixels.Length : 0);
    }

    internal static (int Width, int Height) GetPreviewSize(ProjectDocument document, int width, int height)
    {
        var scale = Math.Min((double)width / document.Width, (double)height / document.Height);
        return (Math.Max(1, (int)Math.Round(document.Width * scale)),
            Math.Max(1, (int)Math.Round(document.Height * scale)));
    }

    private SdrVideoFrame CompleteFrame(SdrVideoFrame presented, SdrVideoFrame background, ProjectPreviewState state, MediaTime time)
    {
        previewFrames?.Register(presented, background, ReferenceEquals(state.Document, failedDocument) ? null : state.Document,
            time, state.IsInteractive, state.QualityRevision);
        return presented;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            renderer?.Dispose();
        }
        finally
        {
            foreach (var converter in converters.Values)
            {
                converter.Dispose();
            }
        }
    }
}
