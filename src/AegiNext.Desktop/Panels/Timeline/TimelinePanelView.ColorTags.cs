using AegiNext.Desktop.Controls.Common;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView
{
    private readonly SubtitleColorTagMenu colorTagMenu = new("TimelineColorTagMenuItem");

    private void RefreshColorTagMenu()
    {
        var document = session.DocumentSnapshot;
        var clipIds = session.TimelineClipIds().ToHashSet();
        var ids = document.Layers.Where(layer => clipIds.Contains(layer.Id)).Select(layer => layer.SubtitleId)
            .OfType<Guid>().Distinct().ToArray();
        var selected = document.Subtitles.Where(line => ids.Contains(line.Id)).Select(line => line.ColorTagId).Distinct().ToArray();
        colorTagMenu.Refresh(document.ColorTags, session.ApplicationContext.ColorTagLibrary.Snapshot.Tags,
            selected.FirstOrDefault(), selected.Length == 1,
            () => ids.Length > 0 && ReferenceEquals(document, session.DocumentSnapshot) && !session.IsClosing && !session.IsProjectBusy,
            tagId => session.SetSubtitleColorTagAsync(ids, tagId, document),
            template => session.ApplySubtitleColorTagAsync(ids, template, document), session.OpenSubtitleColorTagSettingsAsync);
    }
}
