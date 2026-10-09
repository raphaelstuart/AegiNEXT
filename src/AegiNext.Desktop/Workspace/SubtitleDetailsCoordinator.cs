using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Application.SubtitleFormats;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed class SubtitleDetailsCoordinator : IDisposable
{
    private readonly WorkbenchSession session;
    private readonly SemaphoreSlim playbackGate = new(1, 1);
    private readonly Guid customHighlightId = Guid.NewGuid();
    private SubtitleLine? original;
    private SubtitleLine? draft;
    private string? validationError;
    private string source = string.Empty;
    private bool sourceDirty;
    private AssTextEditResult? sourceEdit;
    private ProjectLayer? SourceLayer => session.Editor.Snapshot.Layers.FirstOrDefault(layer => layer.SubtitleId == original?.Id);
    private ImmutableArray<AssSourceMapEntry> sourceMap = [];
    private int styleSelectionStart;
    private int styleSelectionLength;
    private bool committing;
    private bool disposed;
    private Guid? durationClipId;
    private string durationText = string.Empty;
    private bool durationDirty;
    private string leadingDelayText = string.Empty;
    private bool leadingDelayDirty;
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
    internal string Source => source;
    internal ImmutableArray<AssSourceMapEntry> SourceMap => sourceMap;
    internal string DurationText => durationText;
    internal string LeadingDelayText => leadingDelayText;
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
    internal string? SourceDiagnostic { get; private set; }
    internal bool HasDrafts => draft is not null && (sourceDirty || durationDirty || leadingDelayDirty || draft != original ||
        StyleDraft.IsDirty || HighlightDraft.IsDirty);
    internal bool CanEditSource => SourceDiagnostic is null;
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
        if (HighlightDraft.IsDirty && !draft.Karaoke.IsEmpty)
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
        if (sourceDirty && sourceEdit is { } edited)
        {
            return ProjectEditingOperations.ApplyAssTextEdit(document, draft.Id, edited with { Line = draft });
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
        sourceDirty = durationDirty = leadingDelayDirty = false;
        Error = null;
        RefreshSource();
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
        if (field is "Text" or "Code" or "Duration")
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
                RefreshSource();
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
        if (draft is null || original is null || committing)
        {
            return true;
        }
        if (field == "Code" && !sourceDirty || field == "Duration" && !durationDirty)
        {
            return true;
        }
        var candidate = draft;
        MediaTime? duration = null;
        try
        {
            if (field == "Code" && sourceDirty)
            {
                var parsed = AssTextProjection.Apply(original, source, ContentOrigin,
                    session.Editor.Snapshot.Width, session.Editor.Snapshot.Height, layer: SourceLayer);
                if (!parsed.Diagnostics.IsEmpty)
                {
                    throw new InvalidDataException(string.Join(Environment.NewLine,
                        parsed.Diagnostics.Select(item => item.Code + ": " + item.Message)));
                }
                candidate = parsed.Line;
                sourceEdit = parsed;
            }
            else if (field == "Duration" && durationDirty && durationClipId is not null)
            {
                if (!decimal.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                {
                    throw new InvalidDataException(Localization.Get("Workbench.KaraokeDurationInvalid"));
                }
                duration = TimelineTimeText.Parse(seconds.ToString("0.############################", CultureInfo.InvariantCulture));
            }
            else if (field == "Text" && InvalidFieldKey == "Text")
            {
                throw new InvalidDataException(Error);
            }
            ProjectValidator.Validate(session.Editor.Snapshot with
            {
                Subtitles = session.Editor.Snapshot.Subtitles.Select(line => line.Id == candidate.Id ? candidate : line).ToImmutableArray()
            });
        }
        catch (InvalidOperationException error)
        {
            InvalidFieldKey = field;
            Error = error.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or FormatException or OverflowException)
        {
            if (field == "Duration")
            {
                durationDirty = false;
                RefreshDuration();
            }
            else
            {
                draft = original;
                sourceDirty = false;
            sourceEdit = null;
                RefreshSource();
            }
            Error = null;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        var document = session.Editor.Snapshot;
        try
        {
            if (document.Subtitles.FirstOrDefault(line => line.Id == draft.Id) != original)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
            }
            var prepared = field == "Code" && sourceEdit is { } assEdit
                ? ProjectEditingOperations.ApplyAssTextEdit(document, candidate.Id, assEdit with { Line = candidate })
                : field == "Duration" || candidate == original ? document : document with
                {
                    Subtitles = document.Subtitles.Select(line => line.Id == candidate.Id ? candidate : line).ToImmutableArray()
                };
            if (duration is { } length && durationClipId is { } id)
            {
                prepared = ProjectEditingOperations.SetKaraokeClipDuration(prepared, candidate.Id, id, length);
                if (candidate != original)
                {
                    candidate = ProjectEditingOperations.SetKaraokeClipDuration(document with
                    {
                        Subtitles = document.Subtitles.Select(line => line.Id == candidate.Id ? candidate : line).ToImmutableArray()
                    }, candidate.Id, id, length).Subtitles.Single(line => line.Id == candidate.Id);
                }
            }
            ProjectValidator.Validate(prepared);
            committing = true;
            try
            {
                if (prepared != document)
                {
                    session.Editor.Apply("Edit subtitle detail field", _ => prepared);
                }
                var hasPendingContent = candidate != draft || draft != original;
                original = session.Editor.Snapshot.Subtitles.Single(line => line.Id == candidate.Id);
                draft = field == "Duration" && hasPendingContent ? candidate : original;
                if (field == "Duration")
                {
                    durationDirty = false;
                }
                else
                {
                    sourceDirty = false;
                    sourceEdit = null;
                }
                Error = null;
                RefreshSource();
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
            InvalidFieldKey = field;
            Error = error.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    internal void EditSource(string value)
    {
        source = value;
        sourceDirty = true;
        if (draft is null)
        {
            return;
        }
        try
        {
            var result = AssTextProjection.Apply(original!, value, ContentOrigin, session.Editor.Snapshot.Width, session.Editor.Snapshot.Height, layer: SourceLayer);
            if (!result.Diagnostics.IsEmpty)
            {
                InvalidFieldKey = "Code";
                Error = string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + ": " + item.Message));
            }
            else
            {
                draft = result.Line;
                sourceEdit = result;
                sourceMap = result.SourceMap;
                Error = null;
            }
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            InvalidFieldKey = "Code";
            Error = error.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
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
        if ((durationDirty || leadingDelayDirty) && !TryCommit())
        {
            return false;
        }
        SelectedClipId = durationClipId = clipId;
        RefreshDuration();
        return true;
    }

    internal void EditLeadingDelay(string value)
    {
        leadingDelayText = value;
        leadingDelayDirty = true;
        Error = null;
    }

    internal void EditDuration(string value)
    {
        durationText = value;
        durationDirty = true;
        Error = null;
    }

    internal bool SetDuration(Guid clipId, MediaTime duration)
    {
        if (!TryCommit())
        {
            return false;
        }
        durationClipId = SelectedClipId = clipId;
        durationText = ((decimal)duration.Numerator / duration.Denominator).ToString(CultureInfo.InvariantCulture);
        durationDirty = true;
        return TryCommit();
    }

    internal bool SetLeadingDelay(MediaTime delay)
    {
        if (!TryCommit() || draft is null || draft.Karaoke.IsEmpty)
        {
            return false;
        }
        session.Editor.SetKaraokeLeadingDelay(draft.Id, delay, draft.Start - ContentOrigin);
        return true;
    }

    internal void SetHighlightKind(KaraokeHighlightKind kind)
    {
        if (!TryCommit() || draft is null || SelectedClipId is not { } id)
        {
            return;
        }
        session.Editor.UpdateSubtitle(draft.Id, line => line with
        {
            Karaoke = line.Karaoke.Select(clip => clip.Id == id ? clip with { HighlightKind = kind } : clip).ToImmutableArray()
        });
    }

    internal void SetKaraokeEnabled(bool enabled)
    {
        if (!TryCommit() || draft is null || enabled == IsKaraokeEnabled)
        {
            return;
        }
        session.Editor.SetSubtitleKaraokeEnabled(draft.Id, enabled);
    }

    internal void ApplyHighlightStyle(KaraokeHighlightStyle? style)
    {
        if (!TryCommit() || draft is null || !IsKaraokeEnabled)
        {
            return;
        }
        if (styleSelectionLength > 0)
        {
            if (style is null)
            {
                session.Editor.ClearSubtitleKaraokeStyleRange(draft.Id, styleSelectionStart, styleSelectionLength);
            }
            else
            {
                session.Editor.ApplySubtitleKaraokeStyleRange(draft.Id, styleSelectionStart, styleSelectionLength,
                    KaraokeVisualStyleEdit.FromStyle(AsSubtitleStyle(style)));
            }
        }
        else
        {
            session.Editor.UpdateSubtitle(draft.Id, line => line with { KaraokeStyle = style });
        }
    }

    internal KaraokeHighlightStyle HighlightStyle()
    {
        return HighlightStyle(draft, styleSelectionLength > 0);
    }

    private KaraokeHighlightStyle HighlightStyle(SubtitleLine? line, bool selection)
    {
        var clip = selection
            ? line?.Karaoke.FirstOrDefault(value => value.Utf16Start + value.Utf16Length > styleSelectionStart &&
                value.Utf16Start < styleSelectionStart + styleSelectionLength)
            : null;
        if (clip is null && line?.KaraokeStyle is { } style)
        {
            return style;
        }
        var visual = line?.Style ?? new();
        if (clip is not null)
        {
            var inline = line!.InlineSpans.FirstOrDefault(span => span.Utf16Start <= clip.Utf16Start &&
                span.Utf16Start + span.Utf16Length > clip.Utf16Start);
            visual = KaraokeVisualStyleResolver.ResolveActive(inline?.Style.ApplyTo(visual) ?? visual, line.KaraokeStyle, clip);
        }
        else
        {
            visual = visual with { Fill = line?.Karaoke.FirstOrDefault()?.HighlightColor ?? visual.Fill };
        }
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
        if (styleSelectionLength > 0)
        {
            return ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(document, draft.Id,
                styleSelectionStart, styleSelectionLength, edit);
        }
        var line = document.Subtitles.Single(value => value.Id == draft.Id);
        var current = HighlightStyle(line, false);
        var changed = edit.ToOverride(AsSubtitleStyle(current)).ApplyTo(AsSubtitleStyle(current));
        var style = KaraokeHighlightStyle.FromStyle(current.PresetId, current.PresetName, changed);
        return line.KaraokeStyle == style ? document : document with
        {
            Subtitles = document.Subtitles.SetItem(document.Subtitles.IndexOf(line), line with { KaraokeStyle = style })
        };
    }

    private static SubtitleStyle AsSubtitleStyle(KaraokeHighlightStyle value) => new()
    {
        Fill = value.Fill, Stroke = value.Stroke, StrokeWidth = value.StrokeWidth,
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
            if (sourceDirty)
            {
                validatingField = "Code";
                var parsed = AssTextProjection.Apply(original, source, ContentOrigin, document.Width, document.Height, layer: SourceLayer);
                if (!parsed.Diagnostics.IsEmpty)
                {
                    throw new InvalidDataException(string.Join(Environment.NewLine, parsed.Diagnostics.Select(item => item.Code + ": " + item.Message)));
                }
                draft = parsed.Line;
                sourceEdit = parsed;
            }
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
            if (HighlightDraft.IsDirty && !draft.Karaoke.IsEmpty)
            {
                validatingField = "Highlight.Fill";
                prepared = ApplyHighlightEdit(prepared, HighlightDraft.ReadOverride(AsSubtitleStyle(HighlightStyle())));
            }
            if (durationDirty && durationClipId is { } id)
            {
                validatingField = "Duration";
                if (!decimal.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                {
                    throw new InvalidDataException(Localization.Get("Workbench.KaraokeDurationInvalid"));
                }
                var exact = TimelineTimeText.Parse(seconds.ToString("0.############################", CultureInfo.InvariantCulture));
                prepared = ProjectEditingOperations.SetKaraokeClipDuration(prepared, draft.Id, id, exact);
            }
            if (leadingDelayDirty && draft.Karaoke.Length > 0)
            {
                validatingField = "LeadingDelay";
                var delay = TimelineTimeText.Parse(leadingDelayText);
                if (delay < MediaTime.Zero)
                {
                    throw new InvalidDataException(Localization.Get("Workbench.KaraokeDurationInvalid"));
                }
                prepared = ProjectEditingOperations.SetKaraokeLeadingDelay(prepared, draft.Id, delay, draft.Start - ContentOrigin);
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
            sourceDirty = durationDirty = leadingDelayDirty = false;
            sourceEdit = null;
            LoadSelectionStyle();
            LoadHighlightStyle();
            RefreshSource();
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
        if (field == "LeadingDelay")
        {
            leadingDelayDirty = false;
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
        else if (field == "Duration")
        {
            durationDirty = false;
            RefreshDuration();
        }
        else
        {
            draft = original;
            sourceDirty = false;
            sourceEdit = null;
            LoadSelectionStyle();
            LoadHighlightStyle();
            if (field == "All")
            {
                durationDirty = leadingDelayDirty = false;
                RefreshDuration();
            }
            RefreshSource();
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
            sourceDirty = false;
            sourceEdit = null;
            Error = null;
            RefreshSource();
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            InvalidFieldKey = "Text";
            Error = error.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSource()
    {
        if (!sourceDirty)
        {
            try
            {
                var projection = draft is null ? null : AssTextProjection.Create(draft, ContentOrigin, session.Editor.Snapshot.Width, session.Editor.Snapshot.Height, layer: SourceLayer);
                source = projection?.Source ?? string.Empty;
                sourceMap = projection?.SourceMap ?? [];
                SourceDiagnostic = null;
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException)
            {
                source = string.Empty;
                sourceMap = [];
                SourceDiagnostic = error.Message;
            }
        }
    }

    private void RefreshDuration()
    {
        if (!leadingDelayDirty)
        {
            var first = draft?.Karaoke.FirstOrDefault();
            var delay = first is null || draft is null ? MediaTime.Zero : ContentOrigin + first.Start - draft.Start;
            leadingDelayText = ((decimal)delay.Numerator / delay.Denominator).ToString("0.################", CultureInfo.InvariantCulture);
        }
        if (durationDirty)
        {
            return;
        }
        var clip = draft?.Karaoke.FirstOrDefault(clip => clip.Id == SelectedClipId);
        durationClipId = clip?.Id;
        durationText = clip is null ? string.Empty : ((decimal)(clip.End - clip.Start).Numerator / (clip.End - clip.Start).Denominator)
            .ToString("0.################", CultureInfo.InvariantCulture);
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
