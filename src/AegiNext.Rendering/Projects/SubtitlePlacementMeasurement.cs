using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>与合成器共用排版的字幕测量快照；没有墨迹时 Bounds 为明确的逻辑回退边界。</summary>
public sealed record SubtitlePlacementMeasurement(SubtitlePosition Position, SKRect Bounds, bool HasInk);
