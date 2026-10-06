using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal sealed record AssMaskTransform(MediaTime Start, MediaTime End, double Acceleration, RectangleClipMask Mask);
