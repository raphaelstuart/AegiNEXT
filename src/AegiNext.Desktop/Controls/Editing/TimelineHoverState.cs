using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineHoverState(Point Pointer, TimelineKeyframeMarker? Marker);
