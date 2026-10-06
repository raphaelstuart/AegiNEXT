using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record ProjectPersistenceSaveResult(ProjectPersistenceState State, ProjectDocument PersistedSnapshot);
