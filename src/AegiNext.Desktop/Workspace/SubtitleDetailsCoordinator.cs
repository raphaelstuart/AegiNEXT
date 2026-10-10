using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class SubtitleDetailsCoordinator : IDisposable
{
    private readonly WorkbenchSession session;
    private readonly SemaphoreSlim playbackGate = new(1, 1);
    private readonly Guid customHighlightId = Guid.NewGuid();
    private SubtitleLine? original;
    private SubtitleLine? draft;
    private string? validationError;
    private int styleSelectionStart;
    private int styleSelectionLength;
    private bool committing;
    private bool disposed;
    private CancellationTokenSource? playbackCancellation;
    private long playbackRevision;
    private bool playbackRequested;
    private ProjectDocument? previewSource;
    private ProjectDocument? preview;
    private SubtitleLine? previewLine;
    private int previewRevision;
    private int renderedPreviewRevision;

    internal SubtitleDetailsCoordinator(WorkbenchSession session)
    {
        this.session = session;
        StyleDraft = new(session.Fonts);
        session.SelectionChanged += OnSelectionChanged;
        StyleDraft.Changed += OnStyleDraftChanged;
        HighlightDraft.Changed += OnStyleDraftChanged;
    }

    internal event EventHandler? Changed;
    internal SubtitleLine? Line => draft is null ? null : PreviewDocument.Subtitles.FirstOrDefault(line => line.Id == draft.Id);
    internal SubtitleDetailsStyleDraft StyleDraft { get; }
    internal SubtitleKaraokeStyleDraft HighlightDraft { get; } = new();
    internal KaraokeVisualState? VisualState { get; private set; }
    internal string? Error
    {
        get => validationError;
        private set
        {
            validationError = value;
            if (value is null)
            {
                InvalidFieldKey = null;
            }
        }
    }
    internal string? InvalidFieldKey { get; private set; }
    internal bool HasDrafts => draft is not null && (HasTimingDrafts || draft != original ||
        StyleDraft.IsDirty || HighlightDraft.IsDirty);
    internal Guid? SelectedClipId { get; private set; }
    internal bool IsPlaying => playbackCancellation is { } cancellation && session.Controller.IsPlaybackRangeOwnedBy(cancellation.Token);
    internal bool IsKaraokeEnabled => draft is { Karaoke.IsEmpty: false };
    internal bool SelectionHasTimedKaraoke => draft is not null && draft.Karaoke.Any(clip =>
        styleSelectionLength == 0 || clip.Utf16Start < styleSelectionStart + styleSelectionLength &&
        clip.Utf16Start + clip.Utf16Length > styleSelectionStart);
    internal ProjectDocument PreviewDocument => OverlayPreview(session.Editor.Snapshot);

    internal ProjectDocument OverlayPreview(ProjectDocument document)
    {
        if (draft is null || !document.Subtitles.Any(line => line.Id == draft.Id))
        {
            return document;
        }
        if (ReferenceEquals(document, previewSource) && ReferenceEquals(draft, previewLine) &&
            renderedPreviewRevision == previewRevision)
        {
            return preview!;
        }
        ProjectDocument candidate;
        try
        {
            candidate = OverlayContent(document);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            candidate = ReferenceEquals(document, previewSource) ? preview ?? document : document;
        }
        if (StyleDraft.IsDirty && styleSelectionLength > 0)
        {
            try
            {
                candidate = ProjectEditingOperations.ApplySubtitleInlineStyle(candidate, draft.Id,
                    styleSelectionStart, styleSelectionLength, StyleDraft.ReadPreview(SelectionStyle()));
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException)
            {
            }
        }
        if (HighlightDraft.IsDirty && VisualState is not null && draft.Text.Length > 0)
        {
            try
            {
                candidate = ApplyHighlightEdit(candidate, HighlightDraft.ReadPreview(AsSubtitleStyle(HighlightStyle())));
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException)
            {
            }
        }
        previewSource = document;
        previewLine = draft;
        renderedPreviewRevision = previewRevision;
        preview = candidate;
        return preview;
    }

    private ProjectDocument OverlayContent(ProjectDocument document)
    {
        if (draft is null)
        {
            return document;
        }
        return draft == original ? document : document with { Subtitles = document.Subtitles.Select(line => line.Id == draft.Id ? draft : line).ToImmutableArray() };
    }
    internal MediaTime ContentOrigin => draft is null ? MediaTime.Zero : draft.Start -
        (session.Editor.Snapshot.Layers.FirstOrDefault(layer => layer.SubtitleId == draft.Id)?.AnimationOffset ?? MediaTime.Zero);

    internal void Synchronize()
    {
        if (committing || disposed)
        {
            return;
        }
        var selected = session.SelectedCue;
        if (selected?.Id == original?.Id && selected == original)
        {
            return;
        }
        if (original?.Id != selected?.Id)
        {
            _ = StopPlaybackAsync();
            SelectedClipId = null;
            styleSelectionStart = styleSelectionLength = 0;
        }
        else if (selected?.Start != original?.Start || selected?.End != original?.End || selected?.Karaoke != original?.Karaoke)
        {
            _ = StopPlaybackAsync();
        }
        original = draft = selected;
        LoadSelectionStyle();
        LoadHighlightStyle();
        ClearTimingDrafts();
        Error = null;
        RefreshDuration();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void EditText(int start, int length, string replacement)
    {
        Edit(() => ProjectEditingOperations.ReplaceSubtitleTextRange(OverlayContent(session.Editor.Snapshot), draft!.Id, start, length, replacement)
            .Subtitles.Single(line => line.Id == draft.Id));
    }

    internal void ApplySelectionStyle(int start, int length, SubtitleInlineStyleOverride style)
    {
        if (length == 0 || !TryCommit())
        {
            return;
        }
        session.Editor.ApplySubtitleInlineStyle(draft!.Id, start, length, style);
    }

    internal void ApplySelectionFormatting(int start, int length, Func<SubtitleStyle, SubtitleInlineStyleOverride> createEdit)
    {
        if (length == 0 || draft is null || committing)
        {
            return;
        }
        var id = draft.Id;
        var document = session.Editor.Snapshot;
        if (!TryPrepare(document, out var prepared))
        {
            return;
        }
        var line = prepared.Subtitles.Single(line => line.Id == id);
        var inline = line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= start && span.Utf16Start + span.Utf16Length > start);
        var style = inline?.Style.ApplyTo(line.Style) ?? line.Style;
        prepared = ProjectEditingOperations.ApplySubtitleInlineStyle(prepared, id, start, length, createEdit(style));
        ProjectValidator.Validate(prepared);
        CommitPreparedDetails(document, prepared, id, "Format subtitle selection");
    }

    internal void ClearSelectionStyle(int start, int length)
    {
        if (length > 0 && TryCommit())
        {
            session.Editor.ClearSubtitleInlineStyle(draft!.Id, start, length);
        }
    }

    internal void RestoreStyleField(string field, bool highlight)
    {
        if (highlight)
        {
            HighlightDraft.RestoreField(field);
        }
        else
        {
            StyleDraft.RestoreField(field);
        }
        Error = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal bool CompleteInput(string field, bool highlight)
    {
        if (field is "Text" or "Duration" or "Start" or "End" or "LeadingDelay")
        {
            return CompleteContentInput(field);
        }
        if (draft is null || original is null || committing ||
            !(highlight ? HighlightDraft.IsFieldDirty(field) : StyleDraft.IsFieldDirty(field)))
        {
            return true;
        }
        SubtitleInlineStyleOverride? ordinaryEdit = null;
        KaraokeVisualStyleEdit? highlightEdit = null;
        try
        {
            if (highlight)
            {
                highlightEdit = HighlightDraft.ReadOverride(AsSubtitleStyle(HighlightStyle()), field);
            }
            else
            {
                ordinaryEdit = StyleDraft.Read(SelectionStyle(), field);
            }
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or FormatException or OverflowException)
        {
            RestoreStyleField(field, highlight);
            return true;
        }
        var document = session.Editor.Snapshot;
        try
        {
            if (document.Subtitles.FirstOrDefault(line => line.Id == draft.Id) != original)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
            }
            var pending = OverlayContent(document);
            var prepared = document;
            var rebased = pending;
            if (highlightEdit is not null)
            {
                prepared = ApplyHighlightEdit(prepared, highlightEdit);
                rebased = ReferenceEquals(pending, document) ? prepared : ApplyHighlightEdit(pending, highlightEdit);
            }
            else if (ordinaryEdit is { HasOverrides: true })
            {
                if (styleSelectionLength <= 0)
                {
                    throw new InvalidDataException(Localization.Get("Workbench.SelectTextForStyle"));
                }
                prepared = ProjectEditingOperations.ApplySubtitleInlineStyle(prepared, draft.Id,
                    styleSelectionStart, styleSelectionLength, ordinaryEdit);
                rebased = ReferenceEquals(pending, document) ? prepared : ProjectEditingOperations.ApplySubtitleInlineStyle(
                    pending, draft.Id, styleSelectionStart, styleSelectionLength, ordinaryEdit);
            }
            ProjectValidator.Validate(prepared);
            ProjectValidator.Validate(rebased);
            committing = true;
            try
            {
                if (prepared != document)
                {
                    session.Editor.Apply("Edit subtitle detail field", _ => prepared);
                }
                original = session.Editor.Snapshot.Subtitles.Single(line => line.Id == draft.Id);
                draft = ReferenceEquals(pending, document) ? original : rebased.Subtitles.Single(line => line.Id == original.Id);
                if (highlight)
                {
                    HighlightDraft.AcceptField(field, HighlightStyle());
                }
                else
                {
                    StyleDraft.AcceptField(field, SelectionStyle());
                }
                Error = null;
                RefreshDuration();
            }
            finally
            {
                committing = false;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            if (error is not InvalidOperationException)
            {
                RestoreStyleField(field, highlight);
                return true;
            }
            InvalidFieldKey = (highlight ? "Highlight." : "Selection.") + field;
            Error = error.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    private bool CompleteContentInput(string field)
    {
        if (draft is null || original is null || committing || field != "Text" && !IsTimingFieldDirty(field))
        {
            return true;
        }
        var document = session.Editor.Snapshot;
        try
        {
            if (document.Subtitles.FirstOrDefault(line => line.Id == draft.Id) != original)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
            }
            if (field == "Text" && InvalidFieldKey == "Text")
            {
                throw new InvalidDataException(Error);
            }
            var pending = OverlayContent(document);
            var prepared = field == "Text" ? pending : ApplyTimingDraft(document, field);
            var rebased = field == "Text" || ReferenceEquals(pending, document)
                ? prepared : ApplyTimingDraft(pending, field);
            ProjectValidator.Validate(prepared);
            ProjectValidator.Validate(rebased);
            committing = true;
            try
            {
                if (prepared != document)
                {
                    session.Editor.Apply("Edit subtitle detail field", _ => prepared);
                }
                original = session.Editor.Snapshot.Subtitles.Single(line => line.Id == draft.Id);
                draft = field == "Text" || ReferenceEquals(pending, document)
                    ? original : rebased.Subtitles.Single(line => line.Id == original.Id);
                ClearTimingField(field);
                Error = null;
                RefreshDuration();
            }
            finally
            {
                committing = false;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (InvalidOperationException error)
        {
            InvalidFieldKey = field;
            Error = error.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            if (field == "Text")
            {
                draft = original;
            }
            else
            {
                ClearTimingField(field);
                RefreshDuration();
            }
            Error = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    internal bool SelectClip(Guid? clipId)
    {
        if (!TryCommit())
        {
            return false;
        }
        SelectedClipId = clipId;
        durationClipId = clipId;
        RefreshDuration();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal bool FollowTextSelection(int start, int length)
    {
        var end = start + length;
        var clip = draft?.Karaoke.FirstOrDefault(value => value.Utf16Start <= start && value.Utf16Start + value.Utf16Length >= end &&
            (length > 0 || start < value.Utf16Start + value.Utf16Length || start == draft.Text.Length));
        var clipId = clip?.Id;
        if (clipId == SelectedClipId)
        {
            return true;
        }
        if (HasTimingDrafts && !TryCommit())
        {
            return false;
        }
        SelectedClipId = durationClipId = clipId;
        RefreshDuration();
        return true;
    }

    internal void SetHighlightKind(KaraokeHighlightKind kind)
    {
        if (draft is null || SelectedClipId is not { } id)
        {
            return;
        }
        ExecuteTiming("Edit karaoke highlight mode", document =>
        {
            var line = document.Subtitles.Single(line => line.Id == draft.Id);
            var changed = line with
            {
                Karaoke = line.Karaoke.Select(clip => clip.Id == id ? clip with { HighlightKind = kind } : clip).ToImmutableArray()
            };
            return document with { Subtitles = document.Subtitles.SetItem(document.Subtitles.IndexOf(line), changed) };
        }, "Start");
    }

    internal bool SetVisualState(KaraokeVisualState? state)
    {
        if (state == VisualState)
        {
            return true;
        }
        if (!TryCommit())
        {
            return false;
        }
        VisualState = state;
        LoadHighlightStyle();
        previewRevision++;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal void ApplyHighlightStyle(KaraokeHighlightStyle? style)
    {
        if (draft is null || VisualState is not { } state || draft.Text.Length == 0)
        {
            return;
        }
        ExecuteTiming("Apply subtitle visual style", document =>
        {
            if (styleSelectionLength == 0 && state == KaraokeVisualState.ACTIVE)
            {
                var line = document.Subtitles.Single(line => line.Id == draft.Id);
                return document with { Subtitles = document.Subtitles.SetItem(document.Subtitles.IndexOf(line), line with { KaraokeStyle = style }) };
            }
            var length = styleSelectionLength > 0 ? styleSelectionLength : draft.Text.Length;
            var start = styleSelectionLength > 0 ? styleSelectionStart : 0;
            return style is null
                ? ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(document, draft.Id, start, length, state)
                : ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, draft.Id, start, length, state,
                    KaraokeVisualStyleEdit.FromStyle(AsSubtitleStyle(style)));
        }, "Highlight.Fill");
    }

    internal KaraokeHighlightStyle HighlightStyle()
    {
        var line = draft;
        var offset = Math.Clamp(styleSelectionStart, 0, Math.Max(0, (line?.Text.Length ?? 0) - 1));
        var ordinary = line?.Style ?? new();
        var inline = line?.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && span.Utf16Start + span.Utf16Length > offset);
        ordinary = inline?.Style.ApplyTo(ordinary) ?? ordinary;
        var state = VisualState ?? KaraokeVisualState.ACTIVE;
        var range = line is null ? null : KaraokeVisualStyleResolver.RangeStyleAt(line, offset, state);
        var clip = line?.Karaoke.Concat(line.InactiveKaraoke).FirstOrDefault(value => value.Utf16Start <= offset && value.Utf16Start + value.Utf16Length > offset);
        var visual = state == KaraokeVisualState.ACTIVE
            ? KaraokeVisualStyleResolver.ResolveActive(ordinary, line?.KaraokeStyle, clip, range)
            : range?.ApplyTo(ordinary) ?? ordinary;
        return KaraokeHighlightStyle.FromStyle(line?.KaraokeStyle?.PresetId ?? customHighlightId,
            line?.KaraokeStyle?.PresetName ?? Localization.Get("Workbench.CustomKaraokeStyle"), visual);
    }

    private void LoadHighlightStyle() => HighlightDraft.Load(HighlightStyle());

    private ProjectDocument ApplyHighlightEdit(ProjectDocument document, KaraokeVisualStyleEdit edit)
    {
        if (draft is null || !edit.HasChanges)
        {
            return document;
        }
        if (VisualState is not { } state)
        {
            throw new InvalidDataException("请选择未激活或已激活视觉状态。");
        }
        var length = styleSelectionLength > 0 ? styleSelectionLength : draft.Text.Length;
        var start = styleSelectionLength > 0 ? styleSelectionStart : 0;
        return ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, draft.Id, start, length, state, edit);
    }

    private static SubtitleStyle AsSubtitleStyle(KaraokeHighlightStyle value) => new()
    {
        Fill = value.Fill, Stroke = value.Stroke, StrokeWidth = value.StrokeWidth,
        FillBlur = value.FillBlur, StrokeBlur = value.StrokeBlur,
        ShadowColor = value.ShadowColor, ShadowOffset = value.ShadowOffset, ShadowBlur = value.ShadowBlur
    };

    internal bool TryPrepare(ProjectDocument document, out ProjectDocument preparedDocument)
    {
        preparedDocument = document;
        if (committing || draft is null || original is null)
        {
            InvalidFieldKey = null;
            return true;
        }
        var validatingField = "Text";
        try
        {
            if (Error is not null)
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return false;
            }
            validatingField = "Text";
            var current = document.Subtitles.FirstOrDefault(line => line.Id == draft.Id);
            if (current is null || current != original)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
            }
            var prepared = OverlayContent(document);
            if (StyleDraft.IsDirty)
            {
                if (styleSelectionLength <= 0)
                {
                    throw new InvalidDataException(Localization.Get("Workbench.SelectTextForStyle"));
                }
                validatingField = "Selection.FontFamily";
                var style = StyleDraft.Read(SelectionStyle());
                prepared = ProjectEditingOperations.ApplySubtitleInlineStyle(prepared, draft.Id, styleSelectionStart, styleSelectionLength, style);
            }
            if (HighlightDraft.IsDirty)
            {
                validatingField = "Highlight.Fill";
                prepared = ApplyHighlightEdit(prepared, HighlightDraft.ReadOverride(AsSubtitleStyle(HighlightStyle())));
            }
            if (startDirty || endDirty)
            {
                prepared = ApplyEndpointDrafts(prepared, field => validatingField = field);
            }
            foreach (var field in new[] { "Duration", "LeadingDelay" })
            {
                if (IsTimingFieldDirty(field))
                {
                    validatingField = field;
                    prepared = ApplyTimingDraft(prepared, field);
                }
            }
            if (!ReferenceEquals(prepared, document))
            {
                ProjectValidator.Validate(prepared);
            }
            preparedDocument = prepared;
            Error = null;
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            InvalidFieldKey = validatingField.StartsWith("Selection.", StringComparison.Ordinal) && StyleDraft.InvalidField is { } selectionField
                ? "Selection." + selectionField
                : validatingField.StartsWith("Highlight.", StringComparison.Ordinal) && HighlightDraft.InvalidField is { } highlightField
                    ? "Highlight." + highlightField : validatingField;
            Error = error.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    internal bool TryCommit()
    {
        if (committing || draft is null || original is null)
        {
            return true;
        }
        var document = session.Editor.Snapshot;
        if (!TryPrepare(document, out var prepared))
        {
            return false;
        }
        CommitPreparedDetails(document, prepared, draft.Id, "Edit subtitle details");
        return true;
    }

    private void CommitPreparedDetails(ProjectDocument document, ProjectDocument prepared, Guid id, string description)
    {
        committing = true;
        try
        {
            if (prepared != document)
            {
                session.Editor.Apply(description, _ => prepared);
            }
            original = draft = session.Editor.Snapshot.Subtitles.Single(line => line.Id == id);
            ClearTimingDrafts();
            LoadSelectionStyle();
            LoadHighlightStyle();
            RefreshDuration();
        }
        finally
        {
            committing = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Restore(string field)
    {
        if (field == "Timing")
        {
            ClearTimingDrafts();
            RefreshDuration();
        }
        else if (field is "Start" or "End" or "Duration" or "LeadingDelay")
        {
            ClearTimingField(field);
            RefreshDuration();
        }
        else if (field == "Style")
        {
            LoadSelectionStyle();
        }
        else if (field == "Highlight")
        {
            LoadHighlightStyle();
        }
        else
        {
            draft = original;
            LoadSelectionStyle();
            LoadHighlightStyle();
            if (field == "All")
            {
                ClearTimingDrafts();
                RefreshDuration();
            }
        }
        Error = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal bool SetStyleSelection(int start, int length)
    {
        if (start == styleSelectionStart && length == styleSelectionLength)
        {
            return true;
        }
        if ((StyleDraft.IsDirty || HighlightDraft.IsDirty) && !TryCommit())
        {
            return false;
        }
        styleSelectionStart = start;
        styleSelectionLength = length;
        LoadSelectionStyle();
        LoadHighlightStyle();
        previewRevision++;
        return true;
    }

    internal SubtitleStyle SelectionStyle()
    {
        if (draft is null)
        {
            return new();
        }
        var inline = draft.InlineSpans.FirstOrDefault(span => span.Utf16Start <= styleSelectionStart &&
            span.Utf16Start + span.Utf16Length > styleSelectionStart);
        return inline?.Style.ApplyTo(draft.Style) ?? draft.Style;
    }

    private void LoadSelectionStyle() => StyleDraft.Load(SelectionStyle());

    private void Edit(Func<SubtitleLine> edit)
    {
        if (draft is null || disposed)
        {
            return;
        }
        try
        {
            draft = edit();
            Error = null;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            InvalidFieldKey = "Text";
            Error = error.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal async Task PlayAsync(bool selectedClip, bool loop)
    {
        if (!TryCommit() || draft is null || session.Controller.MediaInfo is null)
        {
            return;
        }
        var start = draft.Start;
        var end = draft.End;
        if (selectedClip && draft.Karaoke.FirstOrDefault(value => value.Id == SelectedClipId) is { } clip)
        {
            start = ContentOrigin + clip.Start;
            end = ContentOrigin + clip.End;
            start = start < draft.Start ? draft.Start : start;
            end = end > draft.End ? draft.End : end;
        }
        if (start >= end)
        {
            return;
        }
        var mediaOrigin = session.Controller.Snapshot.Start ?? MediaTime.Zero;
        playbackRequested = true;
        var revision = ++playbackRevision;
        playbackCancellation?.Cancel();
        await playbackGate.WaitAsync();
        try
        {
            if (revision != playbackRevision || disposed)
            {
                return;
            }
            if (playbackCancellation is { } previous)
            {
                await session.Controller.ClearPlaybackRangeAsync(previous.Token);
            }
            if (revision != playbackRevision || disposed)
            {
                return;
            }
            playbackCancellation?.Dispose();
            playbackCancellation = new();
            session.ViewModel.Timeline.ResumePlaybackFollow();
            await session.Controller.PlayRangeAsync(mediaOrigin + start, mediaOrigin + end, loop, playbackCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            playbackGate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal Task SetLoopEnabledAsync(bool loop)
    {
        if (playbackCancellation is { } cancellation)
        {
            session.Controller.SetPlaybackRangeLoop(loop, cancellation.Token);
        }
        return Task.CompletedTask;
    }

    internal async Task StopPlaybackAsync()
    {
        if (!playbackRequested && playbackCancellation is null)
        {
            return;
        }
        var revision = ++playbackRevision;
        playbackCancellation?.Cancel();
        await playbackGate.WaitAsync();
        try
        {
            if (revision != playbackRevision)
            {
                return;
            }
            if (playbackCancellation is { } cancellation)
            {
                await session.Controller.ClearPlaybackRangeAsync(cancellation.Token);
            }
            playbackCancellation?.Dispose();
            playbackCancellation = null;
            playbackRequested = false;
        }
        finally
        {
            playbackGate.Release();
            if (!disposed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => Synchronize();

    private void OnStyleDraftChanged(object? sender, EventArgs e)
    {
        Error = null;
        previewRevision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>撤销播放所有权并释放会话选择订阅。</summary>
    public void Dispose()
    {
        disposed = true;
        session.SelectionChanged -= OnSelectionChanged;
        StyleDraft.Changed -= OnStyleDraftChanged;
        HighlightDraft.Changed -= OnStyleDraftChanged;
        playbackCancellation?.Cancel();
        playbackCancellation?.Dispose();
        playbackCancellation = null;
    }
}
