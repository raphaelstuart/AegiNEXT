using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed class FontNamePreviewGeneration(FontNamePreviewRequest request)
{
    internal FontNamePreviewRequest Request { get; } = request;
    internal TaskCompletionSource<FontNamePreview?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Worker { get; set; } = Task.CompletedTask;
}
