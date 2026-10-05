using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleKaraokeSpan(KaraokeSegment Segment, SKRect Bounds, float AdvanceBefore,
    float TotalAdvance, bool RightToLeft, bool StartsRun, bool EndsRun);
