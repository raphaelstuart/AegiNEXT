namespace AegiNext.Desktop.Editing;

internal sealed record SubtitleColorTagFilterChoice(SubtitleColorTagFilter Value, string Name, string? ColorHex = null)
{
    public bool HasColor => ColorHex is not null;
}
