using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controllers;

internal readonly record struct VideoPreviewPipelineEvent(string Stage, MediaTime Time,
    MediaTime? End, MediaTime Position, MediaTime Cost);
