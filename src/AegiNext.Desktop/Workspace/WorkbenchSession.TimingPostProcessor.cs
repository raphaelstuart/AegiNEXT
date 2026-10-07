using System.Collections.Immutable;
using AegiNext.Application.Timing;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private readonly Func<string, int, MediaTime, CancellationToken, Task<VideoTimingIndex>> videoTimingProbe;
    private Task timingProcessingCompletion = Task.CompletedTask;
    private VideoTimingCacheEntry? videoTimingCache;

    private void OnTimingLibrariesBusyChanged(object? sender, EventArgs e)
    {
        if (!closing)
        {
            ViewModel.RefreshCommands();
        }
    }

    internal bool HasApplicableSelectedTimingPostProcessor => !closing && !projectBusy && !styles.IsBusy &&
        SubtitleTimingAssociationResolver.Resolve(editor.Snapshot, SelectedTimelineSubtitleIds(editor.Snapshot),
            styleLibrary.Snapshot.Presets).Values.Any(preset =>
                preset.TimingPostProcessor is { } options && HasTimingStages(options));

    internal Task<int> ApplySelectedTimingPostProcessorAsync(CancellationToken cancellationToken = default)
    {
        return StartTimingProcessing(ApplySelectedTimingPostProcessorCoreAsync, cancellationToken);
    }

    internal Task<int> ApplyTimingPostProcessorAsync(TimingPostProcessorOptions options,
        IReadOnlySet<string> styleNames, bool onlySelected, CancellationToken cancellationToken = default)
    {
        return StartTimingProcessing(token => ApplyTimingPostProcessorCoreAsync(options, styleNames, onlySelected, token),
            cancellationToken);
    }

    private Task<int> StartTimingProcessing(Func<CancellationToken, Task<int>> process, CancellationToken cancellationToken)
    {
        if (!timingProcessingCompletion.IsCompleted || projectBusy || closing || updatingWorkbench || styles.IsBusy)
        {
            return Task.FromResult(0);
        }

        var operation = process(cancellationToken);
        timingProcessingCompletion = operation;
        return operation;
    }

    private async Task<int> ApplyTimingPostProcessorCoreAsync(TimingPostProcessorOptions options,
        IReadOnlySet<string> styleNames, bool onlySelected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(styleNames);
        options.Validate();
        PrepareTimingProcessing(cancellationToken);
        var source = editor.Snapshot;
        var selected = onlySelected ? SelectedSubtitleIds.ToImmutableHashSet() : null;
        var stylesFilter = styleNames.ToImmutableHashSet(StringComparer.Ordinal);
        return await ProcessTimingSnapshotAsync(source, options.KeyframeSnapEnabled,
            (keyframesAvailable, index) => AegiNext.Application.Timing.TimingPostProcessor.Process(source,
                options with { KeyframeSnapEnabled = keyframesAvailable }, stylesFilter, selected, index), cancellationToken);
    }

    private async Task<int> ApplySelectedTimingPostProcessorCoreAsync(CancellationToken cancellationToken)
    {
        PrepareTimingProcessing(cancellationToken);
        var source = editor.Snapshot;
        var selected = SelectedTimelineSubtitleIds(source);
        var associations = SubtitleTimingAssociationResolver.Resolve(source, selected, styleLibrary.Snapshot.Presets)
            .Where(pair => HasTimingStages(pair.Value.TimingPostProcessor!)).ToDictionary();
        var skipped = selected.Count - associations.Count;
        if (associations.Count == 0)
        {
            LogInfo("Timing", Localization.Format("WorkflowLog.TimingPostProcessorCompleted", 0, 0, skipped));
            return 0;
        }

        var requestedKeyframes = associations.Values.Any(preset => preset.TimingPostProcessor!.KeyframeSnapEnabled);
        var skippedKeyframes = 0;
        var count = await ProcessTimingSnapshotAsync(source, requestedKeyframes, (keyframesAvailable, index) =>
        {
            keyframesAvailable = keyframesAvailable && index is { Keyframes.IsEmpty: false };
            skippedKeyframes = keyframesAvailable ? 0 : associations.Values.Count(preset =>
                preset.TimingPostProcessor!.KeyframeSnapEnabled);
            var options = associations.ToDictionary(pair => pair.Key, pair => pair.Value.TimingPostProcessor!);
            var result = AegiNext.Application.Timing.TimingPostProcessor.Process(source, options, index,
                skipUnavailableKeyframes: !keyframesAvailable);
            var subtitles = result.Subtitles.Select(line => associations.TryGetValue(line.Id, out var preset) &&
                line.StylePresetId != preset.Id ? line with { StylePresetId = preset.Id } : line).ToImmutableArray();
            return subtitles.SequenceEqual(result.Subtitles) ? result : result with { Subtitles = subtitles };
        }, cancellationToken);
        LogInfo("Timing", Localization.Format("WorkflowLog.TimingPostProcessorCompleted", count, associations.Count, skipped));
        if (skippedKeyframes > 0)
        {
            LogInfo("Timing", Localization.Format("WorkflowLog.TimingPostProcessorKeyframesSkipped", skippedKeyframes));
        }
        return count;
    }

    private void PrepareTimingProcessing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCommitDrafts())
        {
            throw new InvalidOperationException(Localization.Get("Settings.TimingDraftInvalid"));
        }

        InvalidateTimingSession();
        ViewModel.CancelGestures();
    }

    private HashSet<Guid> SelectedTimelineSubtitleIds(ProjectDocument source)
    {
        var clips = TimelineClipIds().ToHashSet();
        return Flatten(source.Layers).Where(layer => layer.Kind == LayerKind.SUBTITLE && clips.Contains(layer.Id) &&
            layer.SubtitleId.HasValue).Select(layer => layer.SubtitleId!.Value).ToHashSet();
    }

    private static bool HasTimingStages(TimingPostProcessorOptions options)
    {
        return options.LeadInEnabled || options.LeadOutEnabled || options.AdjacencyEnabled || options.KeyframeSnapEnabled;
    }

    private async Task<int> ProcessTimingSnapshotAsync(ProjectDocument source, bool requestKeyframes,
        Func<bool, VideoTimingIndex?, ProjectDocument> process, CancellationToken cancellationToken)
    {
        var generation = projectGeneration;
        var mediaSnapshot = controller.Snapshot;
        var media = controller.MediaInfo;
        var keyframesAvailable = requestKeyframes && mediaSnapshot.FilePath is not null &&
            !mediaSnapshot.IsOpening && mediaSnapshot.Error is null && media is not null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ProjectOperationsToken);
        SetProjectBusy(true);
        try
        {
            VideoTimingIndex? index = null;
            VideoTimingCacheEntry? indexIdentity = null;
            if (keyframesAvailable)
            {
                var path = mediaSnapshot.FilePath!;
                var streamIndex = media!.VideoStreamIndex;
                var origin = media.Start ?? MediaTime.Zero;
                var file = new FileInfo(path);
                var fileLength = file.Exists ? (long?)file.Length : null;
                var lastWriteTime = file.Exists ? (DateTime?)file.LastWriteTimeUtc : null;
                var creationTime = file.Exists ? (DateTime?)file.CreationTimeUtc : null;
                if (videoTimingCache is { } cached && cached.Path == path && cached.StreamIndex == streamIndex &&
                    cached.Origin == origin && cached.Epoch == mediaSnapshot.Epoch && cached.FileLength == fileLength &&
                    cached.LastWriteTime == lastWriteTime && cached.CreationTime == creationTime)
                {
                    index = cached.Index;
                    indexIdentity = cached;
                }
                else
                {
                    videoTimingCache = null;
                    index = await videoTimingProbe(path, streamIndex, origin, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    file.Refresh();
                    if ((file.Exists ? (long?)file.Length : null) != fileLength ||
                        (file.Exists ? (DateTime?)file.LastWriteTimeUtc : null) != lastWriteTime ||
                        (file.Exists ? (DateTime?)file.CreationTimeUtc : null) != creationTime)
                    {
                        throw new OperationCanceledException(Localization.Get("Settings.TimingContextChanged"), lifetime.Token);
                    }

                    indexIdentity = new(path, streamIndex, origin, mediaSnapshot.Epoch, fileLength, lastWriteTime, creationTime, index);
                }
            }

            var result = await Task.Run(() => process(keyframesAvailable, index), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (closing || generation != projectGeneration || !ReferenceEquals(source, editor.Snapshot) ||
                keyframesAvailable && (controller.Snapshot.Epoch != mediaSnapshot.Epoch ||
                    controller.Snapshot.FilePath != mediaSnapshot.FilePath))
            {
                throw new OperationCanceledException(Localization.Get("Settings.TimingContextChanged"), lifetime.Token);
            }

            if (indexIdentity is { } identity)
            {
                var file = new FileInfo(identity.Path);
                if ((file.Exists ? (long?)file.Length : null) != identity.FileLength ||
                    (file.Exists ? (DateTime?)file.LastWriteTimeUtc : null) != identity.LastWriteTime ||
                    (file.Exists ? (DateTime?)file.CreationTimeUtc : null) != identity.CreationTime)
                {
                    videoTimingCache = null;
                    throw new OperationCanceledException(Localization.Get("Settings.TimingContextChanged"), lifetime.Token);
                }

                videoTimingCache = identity;
            }

            var count = source.Subtitles.Zip(result.Subtitles).Count(pair =>
                pair.First.Start != pair.Second.Start || pair.First.End != pair.Second.End);
            editor.Apply("Timing post-processor", _ => result);
            return count;
        }
        finally
        {
            SetProjectBusy(false);
        }
    }

    private static Task<VideoTimingIndex> ProbeVideoTimingAsync(string path, int streamIndex, MediaTime origin,
        CancellationToken cancellationToken)
    {
        var probe = new FfprobeVideoTimingProbe(new(MediaToolchain.ResolveFfprobe(), TimeSpan.FromMinutes(10),
            maximumOutputCharacters: 128 * 1024 * 1024));
        return probe.ProbeAsync(path, streamIndex, origin, cancellationToken);
    }
}
