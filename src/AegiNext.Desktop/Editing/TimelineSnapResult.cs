using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Editing;

internal readonly record struct TimelineSnapResult(MediaTime Value, MediaTime? Boundary);
