using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WindowChromeProbeReport
{
    public int Version { get; } = 1;
    public string Platform { get; } = RuntimeInformation.OSDescription;
    public string Architecture { get; } = RuntimeInformation.ProcessArchitecture.ToString();
    public List<WindowChromeProbeSample> Samples { get; } = [];
    public List<string> Failures { get; } = [];
    public bool AutomaticChecksCompleted { get; set; }
    public string NativeInteractionStatus { get; } = "Requires manual verification";
}
