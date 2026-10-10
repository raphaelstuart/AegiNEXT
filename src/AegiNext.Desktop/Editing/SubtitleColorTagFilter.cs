using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

internal readonly record struct SubtitleColorTagFilter(bool IsAll, Guid? TagId)
{
    internal static SubtitleColorTagFilter All => new(true, null);
    internal static SubtitleColorTagFilter Untagged => new(false, null);

    internal bool Matches(SubtitleLine line) => IsAll || line.ColorTagId == TagId;
}
