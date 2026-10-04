using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineRow(Guid Id, Guid? TrackId, string Name, IReadOnlyList<ProjectLayer> Clips,
    int Depth, bool IsGroup, bool IsCollapsed, double Top, double Height, double CurveHeight);
