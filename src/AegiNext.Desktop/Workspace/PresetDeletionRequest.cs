using System.Collections.Immutable;

namespace AegiNext.Desktop.Workspace;

internal sealed record PresetDeletionRequest(ImmutableArray<string> Names, bool IsDraftOnly = false);
