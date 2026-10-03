using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>某一工程时刻的局部图层结果；树结构保留组的隔离合成与蒙版顺序。</summary>
public sealed record EvaluatedLayer(ProjectLayer Source, MediaTime LocalTime, LayerTransform Transform,
    double Opacity, SceneColor Fill, SceneColor Stroke, double StrokeWidth, double Blur,
    SubtitleLine? Subtitle, ImmutableArray<EvaluatedLayer> Children);
