using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleLayoutKey(SubtitleLine Subtitle, int Width, int Height, double? LetterSpacing = null);
