namespace AegiNext.Media.Probing;

internal sealed record ProbeProcessOutput(int ExitCode, string StandardOutput, string StandardError);
