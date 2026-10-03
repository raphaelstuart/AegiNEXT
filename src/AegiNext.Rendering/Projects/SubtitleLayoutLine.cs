namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleLayoutLine(string Text, int Utf16Offset, ShapedTextRun? Run);
