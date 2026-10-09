using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class FontNamePreviewTestProvider : IFontNamePreviewProvider
{
    internal bool Deferred { get; set; }
    internal FontNamePreview? Result { get; set; } = new(80, 20, 80, 20, Enumerable.Repeat((byte)255, 1600).ToArray());
    internal List<FontNamePreviewRequest> Requests { get; } = [];
    internal List<CancellationToken> Tokens { get; } = [];
    internal TaskCompletionSource<FontNamePreview?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public ValueTask<FontNamePreview?> GetPreviewAsync(FontNamePreviewRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Tokens.Add(cancellationToken);
        return Deferred ? new(Completion.Task) : ValueTask.FromResult(Result);
    }
}
