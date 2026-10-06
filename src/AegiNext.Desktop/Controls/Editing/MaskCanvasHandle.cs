using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record MaskCanvasHandle(AnimationTrackTarget Target, Point Position, int Corner = -1);
