using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed record SubtitleConversionReview(ImmutableArray<SubtitleFormatDiagnostic> Diagnostics,
    ImmutableArray<SubtitleLine> Subtitles)
{
    private const int PREVIEW_ELEMENT_LIMIT = 120;

    internal int AffectedSubtitleCount => Diagnostics.Where(item => item.SubtitleId.HasValue)
        .Select(item => item.SubtitleId).Distinct().Count();

    internal string FormatSummary()
    {
        return Localization.Format("Workbench.SubtitleConversionSummary", Diagnostics.Length, AffectedSubtitleCount);
    }

    internal string FormatDetails()
    {
        var positions = Subtitles.Select((line, index) => (Line: line, Number: index + 1))
            .ToDictionary(item => item.Line.Id);
        var result = new StringBuilder();
        foreach (var group in Diagnostics.GroupBy(item => item.SubtitleId).OrderBy(group =>
            group.Key is { } id && positions.TryGetValue(id, out var position) ? position.Number : int.MaxValue))
        {
            if (result.Length > 0)
            {
                result.AppendLine();
            }
            if (group.Key is { } id && positions.TryGetValue(id, out var position))
            {
                result.AppendLine(Localization.Format("Workbench.SubtitleConversionPosition", position.Number,
                    TimelineTimeText.Format(position.Line.Start), TimelineTimeText.Format(position.Line.End)));
                result.AppendLine(PreviewText(position.Line.Text));
            }
            else
            {
                result.AppendLine(group.Key is { } unknownId
                    ? Localization.Format("Workbench.SubtitleConversionUnknownPosition", unknownId)
                    : Localization.Get("Workbench.SubtitleConversionGlobal"));
            }
            foreach (var diagnostic in group)
            {
                result.Append("  ").Append(diagnostic.Code).Append(": ").AppendLine(diagnostic.Message);
            }
        }
        return result.ToString().TrimEnd();
    }

    private static string PreviewText(string text)
    {
        var result = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        var count = 0;
        while (elements.MoveNext())
        {
            if (count++ == PREVIEW_ELEMENT_LIMIT)
            {
                result.Append('…');
                break;
            }
            result.Append(elements.GetTextElement().Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
        }
        return result.ToString();
    }
}
