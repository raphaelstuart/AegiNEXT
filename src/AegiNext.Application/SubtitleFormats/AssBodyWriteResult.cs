using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal sealed record AssBodyWriteResult(string Text, ImmutableArray<SubtitleFormatDiagnostic> Diagnostics, MediaTime End);
