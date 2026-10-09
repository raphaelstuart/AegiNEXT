using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

internal sealed class RecordingSubtitlePlacementMeasurer(SubtitlePlacementMetrics metrics) : ISubtitlePlacementMeasurer
{
    internal ProjectDocument? Document { get; private set; }
    internal SubtitleLine? Line { get; private set; }

    /// <summary>记录传入的真实测量对象，并返回固定度量。</summary>
    public SubtitlePlacementMetrics Measure(ProjectDocument document, SubtitleLine line)
    {
        Document = document;
        Line = line;
        return metrics;
    }
}
