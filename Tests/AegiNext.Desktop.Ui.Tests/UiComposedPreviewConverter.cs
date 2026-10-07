using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class UiComposedPreviewConverter(WorkbenchSession session) : IVideoPreviewConverter
{
    private readonly PreviewFrameCatalog catalog = session.PreviewFrames;

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = session.GetPreviewState();
        var marker = frame.CopyPlane(0)[0];
        var background = new SdrVideoFrame(1, 1, [0, 0, 255, 255]);
        var presented = new SdrVideoFrame(1, 1, [0, Green(marker, state.Quality), 0, 255]);
        var sourceTime = (frame.Info.PresentationTimestamp ?? frame.Info.BestEffortTimestamp
            ?? throw new InvalidDataException("The test frame must have a presentation timestamp.")).ToMediaTime();
        var evaluation = state.IsInteractive && state.TargetTime is { } target ? target :
            sourceTime - (state.Document.Media?.MediaOrigin ?? MediaTime.Zero);
        catalog.Register(presented, background, state.Document, evaluation, state.IsInteractive, state.QualityRevision);
        return presented;
    }

    internal static byte Green(byte marker, PreviewQuality quality) => checked((byte)(marker +
        (quality == PreviewQuality.STANDARD ? 180 : 100)));

    public void Dispose()
    {
    }
}
