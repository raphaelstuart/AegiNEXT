using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal sealed record ProjectHistoryEntry(string Label, ProjectDocument Before, ProjectDocument After);
