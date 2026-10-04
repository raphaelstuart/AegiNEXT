using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Rendering;

internal sealed record ProjectPreviewState(ProjectDocument Document, string Directory, MediaTime? TargetTime = null, bool IsInteractive = false);
