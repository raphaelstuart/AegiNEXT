using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

internal readonly record struct TimelineClipRange(MediaTime Start, MediaTime End, bool IsSelected, bool IsInvalid);
