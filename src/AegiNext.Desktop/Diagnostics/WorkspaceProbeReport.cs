using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WorkspaceProbeReport
{
    public int Version { get; } = 1;
    public string Platform { get; } = RuntimeInformation.OSDescription;
    public string Architecture { get; } = RuntimeInformation.ProcessArchitecture.ToString();
    public List<string> Checks { get; } = [];
    public List<string> Failures { get; } = [];
    public List<WorkspaceProbeSample> Samples { get; } = [];
    public List<WorkspaceDockProbeSample> DockSamples { get; } = [];
    public bool Completed { get; set; }
    public string ManualInteractionStatus { get; } = "Native button clicks, dragging, fullscreen and cross-display DPI require manual acceptance.";
}
