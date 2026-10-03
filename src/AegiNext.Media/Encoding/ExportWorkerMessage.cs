namespace AegiNext.Media.Encoding;

internal sealed record ExportWorkerMessage(string Type, ulong Frames = 0, long PositionNumerator = 0,
    long PositionDenominator = 1, string? Error = null);
