using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed class LayerPlacementResolver : IDisposable
{
    private ProjectSceneRenderer? renderer;
    private string? directory;
    private ProjectDocument? cachedDocument;
    private readonly Dictionary<Guid, LayerPlacementResolution> cache = [];

    internal LayerPlacementResolution Resolve(ProjectDocument document, string projectDirectory, ProjectLayer? layer)
    {
        if (layer?.SubtitleId is not { } id)
        {
            return new(new ScenePoint(), null);
        }

        if (renderer is null || directory != projectDirectory)
        {
            renderer?.Dispose();
            renderer = new(new DirectoryProjectAssetResolver(projectDirectory));
            directory = projectDirectory;
            cache.Clear();
        }

        if (!ReferenceEquals(cachedDocument, document))
        {
            cachedDocument = document;
            cache.Clear();
        }

        if (cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var subtitle = document.Subtitles.Single(line => line.Id == id);
        LayerPlacementResolution result;
        try
        {
            var measurement = renderer.MeasureSubtitlePlacement(document, subtitle);
            var position = measurement.Position;
            result = new(new ScenePoint(position.Anchor.X * document.Width + position.Offset.X,
                position.Anchor.Y * document.Height + position.Offset.Y), position, Geometry: new(
                new(document.Width, document.Height), new(measurement.Bounds.Left, measurement.Bounds.Top),
                new(measurement.Bounds.Width, measurement.Bounds.Height), layer.Transform, measurement.HasInk));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            result = new(null, null, error);
        }

        cache.Add(id, result);
        return result;
    }

    public void Dispose()
    {
        renderer?.Dispose();
        renderer = null;
        cachedDocument = null;
        cache.Clear();
    }
}
