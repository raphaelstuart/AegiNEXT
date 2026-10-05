using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

internal static class SubtitleTextDifference
{
    internal static SubtitleLine Apply(ProjectDocument document, SubtitleLine original, string text)
    {
        if (text == original.Text)
        {
            return original;
        }
        var oldText = original.Text;
        var start = 0;
        while (start < oldText.Length && start < text.Length && oldText[start] == text[start])
        {
            start++;
        }
        var oldBoundaries = StringInfo.ParseCombiningCharacters(oldText).Append(oldText.Length).ToHashSet();
        var newBoundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
        while (start > 0 && (!oldBoundaries.Contains(start) || !newBoundaries.Contains(start)))
        {
            start--;
        }
        var oldEnd = oldText.Length;
        var newEnd = text.Length;
        while (oldEnd > start && newEnd > start && oldText[oldEnd - 1] == text[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }
        while (oldEnd < oldText.Length && (!oldBoundaries.Contains(oldEnd) || !newBoundaries.Contains(newEnd)))
        {
            oldEnd++;
            newEnd++;
        }
        return ProjectEditingOperations.ReplaceSubtitleTextRange(document, original.Id, start, oldEnd - start,
            text[start..newEnd]).Subtitles.Single(line => line.Id == original.Id);
    }
}
