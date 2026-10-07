using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;
using Avalonia.Controls;

namespace AegiNext.Desktop.Controls;

internal sealed record PathCanvasTopologyEdit(ProjectDocument Document, ProjectLayer Layer, MediaTime Time,
    bool EditingEndpoint, TopLevel Host, BezierEditAction Action, int Index, double Progress, Point Position);
