using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record MaskCanvasSegmentHandle(Guid ContourId, Guid NodeId, Point Position, double Progress);
