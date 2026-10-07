using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controllers;

internal sealed class VideoPreviewPipelineDiagnostics
{
    private const int EVENT_CAPACITY = 32;
    private readonly Queue<VideoPreviewPipelineEvent> events = new();

    internal long Candidates { get; set; }
    internal long CandidateSkips { get; set; }
    internal long Conversions { get; set; }
    internal long Presented { get; set; }
    internal long Early { get; set; }
    internal long ExpiredBeforeDispatch { get; set; }
    internal long ExpiredInCallback { get; set; }
    internal long SkippedEarlyRetries { get; set; }
    internal long QueuedReclaims { get; set; }
    internal MediaTime LastConversionCost { get; set; }
    internal MediaTime LastDispatchCost { get; set; }

    internal void Record(VideoPreviewPipelineEvent value)
    {
        events.Enqueue(value);
        while (events.Count > EVENT_CAPACITY)
        {
            events.Dequeue();
        }
    }

    internal string Describe(MediaTime preparationLead, MediaTime conversionLead, MediaTime dispatchLead)
    {
        return $"Candidates={Candidates}; CandidateSkips={CandidateSkips}; Conversions={Conversions}; " +
            $"Presented={Presented}; Early={Early}; ExpiredBeforeDispatch={ExpiredBeforeDispatch}; " +
            $"ExpiredInCallback={ExpiredInCallback}; SkippedEarlyRetries={SkippedEarlyRetries}; QueuedReclaims={QueuedReclaims}; LastConversionCost={LastConversionCost}; " +
            $"LastDispatchCost={LastDispatchCost}; PreparationLead={preparationLead}; " +
            $"ConversionLead={conversionLead}; DispatchLead={dispatchLead}; Events=[{string.Join("; ", events)}]";
    }
}
