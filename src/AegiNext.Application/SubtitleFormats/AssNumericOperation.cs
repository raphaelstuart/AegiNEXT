using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal sealed record AssNumericOperation(MediaTime Start, MediaTime End, double Value, double Exponent);
