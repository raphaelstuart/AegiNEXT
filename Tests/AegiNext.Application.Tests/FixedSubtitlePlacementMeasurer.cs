using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

internal sealed class FixedSubtitlePlacementMeasurer(SubtitlePlacementMetrics metrics) : ISubtitlePlacementMeasurer
{
    /// <summary>返回独立于字体环境的排版度量，验证转换层的几何计算。</summary>
    public SubtitlePlacementMetrics Measure(ProjectDocument document, SubtitleLine line) => metrics;
}
