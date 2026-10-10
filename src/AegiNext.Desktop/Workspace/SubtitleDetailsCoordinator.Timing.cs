using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class SubtitleDetailsCoordinator
{
    private Guid? durationClipId;
    private string durationText = string.Empty;
    private string durationBaselineText = string.Empty;
    private bool durationDirty;
    private string startText = string.Empty;
    private string startBaselineText = string.Empty;
    private bool startDirty;
    private string endText = string.Empty;
    private string endBaselineText = string.Empty;
    private bool endDirty;
    private string leadingDelayText = string.Empty;
    private string leadingDelayBaselineText = string.Empty;
    private bool leadingDelayDirty;

    internal string StartText => startText;
    internal string EndText => endText;
    internal string DurationText => durationText;
    internal string LeadingDelayText => leadingDelayText;
    internal bool LinkedDurationEnabled { get; set; }
    internal string? MergeSelectionErrorKey => MergeErrorKey(Line, SelectedClipIds);
    private bool HasTimingDrafts => startDirty || endDirty || durationDirty || leadingDelayDirty;
    private MediaTime AnimationOffset => session.Editor.Snapshot.Layers.FirstOrDefault(layer => layer.SubtitleId == draft?.Id)?.AnimationOffset ?? MediaTime.Zero;

    internal void EditStart(string value)
    {
        startText = value;
        startDirty = value != startBaselineText;
        Error = null;
    }

    internal void EditEnd(string value)
    {
        endText = value;
        endDirty = value != endBaselineText;
        Error = null;
    }

    internal void EditDuration(string value)
    {
        durationText = value;
        durationDirty = value != durationBaselineText;
        Error = null;
    }

    internal void EditLeadingDelay(string value)
    {
        leadingDelayText = value;
        leadingDelayDirty = value != leadingDelayBaselineText;
        Error = null;
    }

    internal bool SetRange(SubtitleLine baselineLine, MediaTime offset, Guid clipId, MediaTime start, MediaTime end)
    {
        if (baselineLine != Line || offset != AnimationOffset)
        {
            return FailTiming("Start", Localization.Get("Workbench.SubtitleDraftConflict"));
        }
        return ExecuteTiming("Edit karaoke range", document => ProjectEditingOperations.SetKaraokeClipRange(
            document, baselineLine.Id, clipId, start, end), "Start");
    }

    internal bool SetDuration(Guid clipId, MediaTime duration)
    {
        return ExecuteTiming("Edit karaoke duration", document => ApplyDuration(document, clipId, duration), "Duration");
    }

    internal bool SetLeadingDelay(MediaTime delay)
    {
        if (draft is null || draft.Karaoke.IsEmpty)
        {
            return false;
        }
        return ExecuteTiming("Edit karaoke leading delay", document => ProjectEditingOperations.SetKaraokeLeadingDelay(
            document, draft.Id, delay, AnimationOffset), "LeadingDelay");
    }

    internal bool CreateTiming(int selectionStart, int length, string start, string end)
    {
        if (draft is null)
        {
            return false;
        }
        var field = "Start";
        var result = ExecuteTiming("Create karaoke timing", document =>
        {
            var contentStart = TimelineTimeText.Parse(start) + AnimationOffset;
            if (contentStart < MediaTime.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "计时组不能早于内容时间原点。");
            }
            field = "End";
            var contentEnd = TimelineTimeText.Parse(end) + AnimationOffset;
            return ProjectEditingOperations.CreateKaraokeClip(document, draft.Id, selectionStart, length, contentStart, contentEnd);
        }, () => field);
        if (result)
        {
            var id = draft!.Karaoke.Single(clip => clip.Utf16Start == selectionStart && clip.Utf16Length == length).Id;
            SetClipSelection([id], id);
            RefreshDuration();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return result;
    }

    internal void DismissCreationError()
    {
        Error = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal bool SplitSelectedGroup()
    {
        if (draft is null || SelectedClipId is not { } clipId)
        {
            return false;
        }
        return ExecuteTiming("Split karaoke group", document => ProjectEditingOperations.SplitKaraokeClipIntoGraphemes(
            document, draft.Id, clipId), "Start");
    }

    internal bool MergeSelectedGroups()
    {
        if (draft is null)
        {
            return false;
        }
        var ids = SelectedClipIds;
        Guid? mergedId = null;
        var result = ExecuteTiming("Merge karaoke groups", document =>
        {
            var line = document.Subtitles.Single(line => line.Id == draft.Id);
            if (MergeErrorKey(line, ids) is { } reason)
            {
                throw new InvalidOperationException(Localization.Get(reason));
            }
            mergedId = ids[0];
            foreach (var id in ids.Skip(1))
            {
                document = ProjectEditingOperations.MergeKaraokeClips(document, draft.Id, ids[0], id);
            }
            return document;
        }, "Start");
        if (result)
        {
            SetClipSelection([mergedId!.Value], mergedId);
            RefreshDuration();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return result;
    }

    private static string? MergeErrorKey(SubtitleLine? line, IReadOnlyList<Guid> ids)
    {
        if (line is null || ids.Count < 2)
        {
            return "Workbench.MergeKaraokeSelectionMultiple";
        }
        var selectedIds = ids.ToHashSet();
        var clips = line.Karaoke.Where(clip => selectedIds.Contains(clip.Id)).ToArray();
        if (clips.Length != ids.Count)
        {
            return "Workbench.SubtitleDraftConflict";
        }
        for (var index = 1; index < clips.Length; index++)
        {
            var first = clips[index - 1];
            var second = clips[index];
            if (first.Utf16Start + first.Utf16Length != second.Utf16Start)
            {
                return "Workbench.MergeKaraokeSelectionContiguous";
            }
            if (first.End != second.Start)
            {
                return "Workbench.MergeKaraokeSelectionTimeAdjacent";
            }
            if (first.HighlightKind != second.HighlightKind)
            {
                return "Workbench.MergeKaraokeSelectionMode";
            }
        }
        return null;
    }

    internal bool GenerateAllTiming()
    {
        if (draft is null || draft.Text.Length == 0)
        {
            return false;
        }
        return ExecuteTiming("Generate karaoke timing", document =>
            ProjectEditingOperations.GenerateSubtitleKaraokeClips(document, draft.Id), "Start");
    }

    internal bool RestoreCachedTiming()
    {
        if (draft is null || draft.InactiveKaraoke.IsEmpty)
        {
            return false;
        }
        return ExecuteTiming("Restore karaoke timing", document => ProjectEditingOperations.SetSubtitleKaraokeEnabled(document, draft.Id, true), "Start");
    }

    internal void SetKaraokeEnabled(bool enabled)
    {
        if (draft is null || enabled == IsKaraokeEnabled)
        {
            return;
        }
        ExecuteTiming("Toggle karaoke timing", document => ProjectEditingOperations.SetSubtitleKaraokeEnabled(document, draft.Id, enabled), "Start");
    }

    private bool ExecuteTiming(string description, Func<ProjectDocument, ProjectDocument> edit, string field)
    {
        return ExecuteTiming(description, edit, () => field);
    }

    private bool ExecuteTiming(string description, Func<ProjectDocument, ProjectDocument> edit, Func<string> field)
    {
        if (draft is null || committing)
        {
            return false;
        }
        var document = session.Editor.Snapshot;
        if (!TryPrepare(document, out var prepared))
        {
            return false;
        }
        try
        {
            prepared = edit(prepared);
            ProjectValidator.Validate(prepared);
            CommitPreparedDetails(document, prepared, draft.Id, description);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            return FailTiming(field(), error.Message);
        }
    }

    private bool FailTiming(string field, string message)
    {
        InvalidFieldKey = field;
        Error = message;
        Changed?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private ProjectDocument ApplyDuration(ProjectDocument document, Guid clipId, MediaTime duration)
    {
        if (duration <= MediaTime.Zero)
        {
            throw new InvalidDataException(Localization.Get("Workbench.KaraokeDurationInvalid"));
        }
        if (LinkedDurationEnabled)
        {
            return ProjectEditingOperations.SetKaraokeClipDuration(document, draft!.Id, clipId, duration);
        }
        var clip = document.Subtitles.Single(line => line.Id == draft!.Id).Karaoke.Single(clip => clip.Id == clipId);
        return ProjectEditingOperations.SetKaraokeClipRange(document, draft!.Id, clipId, clip.Start, clip.Start + duration);
    }

    private ProjectDocument ApplyTimingDraft(ProjectDocument document, string field)
    {
        if (field == "LeadingDelay")
        {
            return ProjectEditingOperations.SetKaraokeLeadingDelay(document, draft!.Id,
                TimelineTimeText.Parse(leadingDelayText), AnimationOffset);
        }
        if (durationClipId is not { } id)
        {
            throw new InvalidOperationException("请选择要编辑的计时组。");
        }
        if (field == "Duration")
        {
            return ApplyDuration(document, id, TimelineTimeText.Parse(durationText));
        }
        var clip = document.Subtitles.Single(line => line.Id == draft!.Id).Karaoke.Single(clip => clip.Id == id);
        var start = field == "Start" ? TimelineTimeText.Parse(startText) + AnimationOffset : clip.Start;
        var end = field == "End" ? TimelineTimeText.Parse(endText) + AnimationOffset : clip.End;
        return ProjectEditingOperations.SetKaraokeClipRange(document, draft!.Id, id, start, end);
    }

    private ProjectDocument ApplyEndpointDrafts(ProjectDocument document, Action<string> validating)
    {
        if (durationClipId is not { } id)
        {
            throw new InvalidOperationException("请选择要编辑的计时组。");
        }
        var clip = document.Subtitles.Single(line => line.Id == draft!.Id).Karaoke.Single(clip => clip.Id == id);
        var start = clip.Start;
        var end = clip.End;
        if (startDirty)
        {
            validating("Start");
            start = TimelineTimeText.Parse(startText) + AnimationOffset;
            if (start < MediaTime.Zero)
            {
                throw new InvalidDataException("计时组不能早于内容时间原点。");
            }
        }
        if (endDirty)
        {
            validating("End");
            end = TimelineTimeText.Parse(endText) + AnimationOffset;
        }
        return ProjectEditingOperations.SetKaraokeClipRange(document, draft!.Id, id, start, end);
    }

    private bool IsTimingFieldDirty(string field) => field switch
    {
        "Start" => startDirty,
        "End" => endDirty,
        "Duration" => durationDirty,
        "LeadingDelay" => leadingDelayDirty,
        _ => false
    };

    private void ClearTimingField(string field)
    {
        switch (field)
        {
            case "Start":
                startDirty = false;
                break;
            case "End":
                endDirty = false;
                break;
            case "Duration":
                durationDirty = false;
                break;
            case "LeadingDelay":
                leadingDelayDirty = false;
                break;
        }
    }

    private void ClearTimingDrafts()
    {
        startDirty = endDirty = durationDirty = leadingDelayDirty = false;
    }

    private void RefreshDuration()
    {
        SetClipSelection(SelectedClipIds, SelectedClipId);
        if (!leadingDelayDirty)
        {
            var earliest = draft?.Karaoke.IsEmpty == false ? draft.Karaoke.Min(clip => clip.Start) : MediaTime.Zero;
            var delay = draft?.Karaoke.IsEmpty == false ? earliest - AnimationOffset : MediaTime.Zero;
            leadingDelayBaselineText = leadingDelayText = FormatTime(delay);
        }
        var clip = draft?.Karaoke.FirstOrDefault(clip => clip.Id == SelectedClipId);
        durationClipId = clip?.Id;
        if (!startDirty)
        {
            startBaselineText = startText = clip is null ? string.Empty : FormatTime(clip.Start - AnimationOffset);
        }
        if (!endDirty)
        {
            endBaselineText = endText = clip is null ? string.Empty : FormatTime(clip.End - AnimationOffset);
        }
        if (!durationDirty)
        {
            durationBaselineText = durationText = clip is null ? string.Empty : FormatTime(clip.End - clip.Start);
        }
    }

    private static string FormatTime(MediaTime time)
    {
        return ((decimal)time.Numerator / time.Denominator).ToString("0.################", CultureInfo.InvariantCulture);
    }
}
