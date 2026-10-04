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
    public bool SourceProfileCopied { get; set; }
    public int LoadedStylePresetCount { get; set; }
    public int SettingsBindingExceptionCount { get; set; }
    public int ExportBindingExceptionCount { get; set; }
    public int GetBindingExceptionCount { get; set; }
    public int ExportCodecChoiceCount { get; set; }
    public int ExportSpeedChoiceCount { get; set; }
    public int ExportAudioModeChoiceCount { get; set; }
    public string ManualInteractionStatus { get; } = "Native button clicks, dragging, fullscreen and cross-display DPI require manual acceptance.";
}
