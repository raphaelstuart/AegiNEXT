using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Avalonia.Media;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed class TimelinePanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private ProjectDocument document = new();
    private Guid? selectedCueId;
    private ProjectLayer? selectedLayer;
    private MediaTime position = MediaTime.Zero;
    private TimelineViewport viewport = new();
    private double mediaDuration;
    private double documentDuration = 10;
    private IReadOnlyList<Guid> selectedLayerIds = [];
    private double scrollMaximum = 1;
    private double viewportSize = 10;
    private bool isSeeking;
    private AnimationProperty effectProperty = AnimationProperty.OPACITY;
    private SpectrogramData? spectrogram;
    private string analysisStatus = string.Empty;

    internal TimelinePanelViewModel(WorkbenchSession session)
    {
        this.session = session;
    }

    public ProjectDocument Document
    {
        get => document;
        set
        {
            if (SetProperty(ref document, value))
            {
                documentDuration = Math.Max(1, Flatten(value.Layers).Select(layer => Seconds(layer.End))
                    .Concat(value.Subtitles.Select(cue => Seconds(cue.End))).DefaultIfEmpty(1).Max());
                OnPropertyChanged(nameof(FullDuration));
            }
        }
    }

    public Guid? SelectedCueId
    {
        get => selectedCueId;
        set => SetProperty(ref selectedCueId, value);
    }

    public ProjectLayer? SelectedLayer
    {
        get => selectedLayer;
        set => SetProperty(ref selectedLayer, value);
    }

    public MediaTime Position
    {
        get => position;
        set => SetProperty(ref position, value);
    }

    public double PixelsPerSecond
    {
        get => viewport.PixelsPerSecond;
        set => Viewport = viewport with { PixelsPerSecond = value };
    }

    public double ViewStart
    {
        get => viewport.StartSeconds;
        set => Viewport = viewport with { StartSeconds = value };
    }

    public double VisibleDuration
    {
        get => viewport.VisibleDuration;
        set => Viewport = viewport with { Width = Math.Max(0, value) * viewport.PixelsPerSecond };
    }

    public TimelineViewport Viewport
    {
        get => viewport;
        set
        {
            if (SetProperty(ref viewport, value))
            {
                OnPropertyChanged(nameof(PixelsPerSecond));
                OnPropertyChanged(nameof(ViewStart));
                OnPropertyChanged(nameof(VisibleDuration));
            }
        }
    }

    public IReadOnlyList<Guid> SelectedLayerIds
    {
        get => selectedLayerIds;
        set => SetProperty(ref selectedLayerIds, value);
    }

    public double MediaDuration
    {
        get => mediaDuration;
        set
        {
            if (SetProperty(ref mediaDuration, value))
            {
                OnPropertyChanged(nameof(FullDuration));
            }
        }
    }

    public double FullDuration => Math.Max(Math.Max(1, mediaDuration), documentDuration);

    public double ScrollMaximum
    {
        get => scrollMaximum;
        set => SetProperty(ref scrollMaximum, value);
    }

    public double ViewportSize
    {
        get => viewportSize;
        set => SetProperty(ref viewportSize, value);
    }

    public bool IsSeeking
    {
        get => isSeeking;
        set => SetProperty(ref isSeeking, value);
    }

    public AnimationProperty EffectProperty
    {
        get => effectProperty;
        set => SetProperty(ref effectProperty, value);
    }

    public SpectrogramData? Spectrogram
    {
        get => spectrogram;
        set => SetProperty(ref spectrogram, value);
    }

    public string AnalysisStatus
    {
        get => analysisStatus;
        set => SetProperty(ref analysisStatus, value);
    }

    /// <summary>提交时间线上的播放定位请求。</summary>
    public Task SeekAsync(MediaTime time) => session.RunCommandAsync(() => session.SeekProjectTimeAsync(time));
    /// <summary>同步字幕选择。</summary>
    public void SelectCue(Guid id) => session.SelectCue(id);
    /// <summary>同步非字幕片段对应的图层选择。</summary>
    public void SelectLayer(Guid id) => session.SelectLayer(id, [id]);
    /// <summary>将时间线的主层和多选集合同步到同一会话选择。</summary>
    public void SelectLayers(TimelineSelectionEventArgs value) => session.SelectLayer(value.Id, value.SelectedIds.ToArray());
    /// <summary>切换当前字幕轨道，不改变工程合成顺序。</summary>
    public void SelectTrack(Guid id) => session.SelectTrack(id);
    /// <summary>一次完成的时间线手势对应一次工程事务。</summary>
    public Task CommitTimingAsync(TimelineTimingEventArgs value)
    {
        if (value.SubtitleId is { } cueId && value.TrackId is { } trackId)
        {
            return session.CommitSubtitleClipMoveAsync(cueId, trackId, value.Start, value.End, value.Mode, value.IsMove);
        }

        return session.RunCommandAsync(() => session.EditAsync(() =>
        {
            if (value.IsMove)
            {
                session.Editor.ShiftLayer(value.Id, value.Start - value.OriginalStart);
            }
            else
            {
                session.Editor.SetLayerTiming(value.Id, value.Start, value.End, value.Mode);
            }
        }));
    }
    /// <summary>选择关键帧并同步属性检查器。</summary>
    public void SelectKeyframe(TimelineKeyframeEventArgs value) => session.SelectKeyframe(value);
    /// <summary>提交完成的关键帧手势。</summary>
    public Task MoveKeyframeAsync(TimelineKeyframeEventArgs value) => session.RunCommandAsync(() => session.EditAsync(() => session.MoveKeyframe(value)));
    /// <summary>视口尺寸改变后重新计算滚动范围。</summary>
    public void RefreshViewport() => session.Tick();

    private static IEnumerable<ProjectLayer> Flatten(IEnumerable<ProjectLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
            {
                yield return child;
            }
        }
    }

    private static double Seconds(MediaTime value) => (double)value.Numerator / value.Denominator;
}
