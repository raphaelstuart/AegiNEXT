using AegiNext.Core.Timing;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record KaraokeDurationLabel(Guid ClipId, MediaTime Duration, string Text, Rect Bounds);
