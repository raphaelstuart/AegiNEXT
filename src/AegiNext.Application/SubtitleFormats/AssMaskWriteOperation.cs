using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal sealed record AssMaskWriteOperation(MediaTime Start, MediaTime End, double Acceleration, ScenePoint TopLeft, ScenePoint BottomRight);
