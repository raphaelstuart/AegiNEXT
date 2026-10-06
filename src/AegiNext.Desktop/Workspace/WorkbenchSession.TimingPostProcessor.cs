using System.Collections.Immutable;
using AegiNext.Application.Timing;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private readonly Func<string, int, MediaTime, CancellationToken, Task<VideoTimingIndex>> videoTimingProbe;
    private Task timingProcessingCompletion = Task.CompletedTask;
    private VideoTimingCacheEntry? videoTimingCache;

    internal Task<int> ApplyTimingPostProcessorAsync(TimingPostProcessorOptions options,
        IReadOnlySet<string> styleNames, bool onlySelected, CancellationToken cancellationToken = default)
    {
        if (!timingProcessingCompletion.IsCompleted || projectBusy || closing || updatingWorkbench)
        {
            return Task.FromResult(0);
        }

        var operation = ApplyTimingPostProcessorCoreAsync(options, styleNames, onlySelected, cancellationToken);
        timingProcessingCompletion = operation;
        return operation;
    }

    private async Task<int> ApplyTimingPostProcessorCoreAsync(TimingPostProcessorOptions options,
        IReadOnlySet<string> styleNames, bool onlySelected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(styleNames);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCommitDrafts())
        {
            throw new InvalidOperationException(Localization.Get("Settings.TimingDraftInvalid"));
        }

        InvalidateTimingSession();
        var source = editor.Snapshot;
        var generation = projectGeneration;
        var selected = onlySelected ? SelectedSubtitleIds.ToImmutableHashSet() : null;
        var stylesFilter = styleNames.ToImmutableHashSet(StringComparer.Ordinal);
        var mediaSnapshot = controller.Snapshot;
        var media = controller.MediaInfo;
        var effectiveOptions = options with
        {
            KeyframeSnapEnabled = options.KeyframeSnapEnabled && mediaSnapshot.FilePath is not null &&
                !mediaSnapshot.IsOpening && mediaSnapshot.Error is null && media is not null
        };
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ProjectOperationsToken);
        SetProjectBusy(true);
        try
        {
            VideoTimingIndex? index = null;
            VideoTimingCacheEntry? indexIdentity = null;
            if (effectiveOptions.KeyframeSnapEnabled)
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

            var result = await Task.Run(() =>
                AegiNext.Application.Timing.TimingPostProcessor.Process(source, effectiveOptions, stylesFilter, selected, index),
                lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (closing || generation != projectGeneration || !ReferenceEquals(source, editor.Snapshot) ||
                effectiveOptions.KeyframeSnapEnabled && (controller.Snapshot.Epoch != mediaSnapshot.Epoch ||
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
