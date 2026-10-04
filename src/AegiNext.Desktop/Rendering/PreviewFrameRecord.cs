using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record PreviewFrameRecord(SdrVideoFrame Background, ProjectDocument? Document, MediaTime? Time, bool Interactive);
