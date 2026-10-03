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
    private double pixelsPerSecond = 48;
    private double viewStart;
    private double visibleDuration = 10;
    private double scrollMaximum = 1;
    private double viewportSize = 10;
    private bool showEffects;
    private bool isSeeking;
    private AnimationProperty effectProperty = AnimationProperty.OPACITY;
    private SpectrogramData? spectrogram;
    private string analysisStatus = string.Empty;

    internal TimelinePanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        SubtitleTimelineCommand = new RelayCommand(() => ShowEffects = false);
        EffectTimelineCommand = new RelayCommand(() => ShowEffects = true);
    }

    public ProjectDocument Document
    {
        get => document;
        set => SetProperty(ref document, value);
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
        get => pixelsPerSecond;
        set => SetProperty(ref pixelsPerSecond, value);
    }

    public double ViewStart
    {
        get => viewStart;
        set => SetProperty(ref viewStart, value);
    }

    public double VisibleDuration
    {
        get => visibleDuration;
        set => SetProperty(ref visibleDuration, value);
    }

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

    public bool ShowEffects
    {
        get => showEffects;
        set => SetProperty(ref showEffects, value);
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

    public ICommand SubtitleTimelineCommand { get; }

    public ICommand EffectTimelineCommand { get; }
    /// <summary>提交时间线上的播放定位请求。</summary>
    public Task SeekAsync(MediaTime time) => session.RunCommandAsync(() => session.SeekProjectTimeAsync(time));
    /// <summary>同步字幕选择。</summary>
    public void SelectCue(Guid id) => session.SelectCue(id);
    /// <summary>一次完成的时间线手势对应一次工程事务。</summary>
    public Task CommitTimingAsync(TimelineTimingEventArgs value) => session.RunCommandAsync(() => session.EditAsync(() =>
    {
        if (value.IsMove)
        {
            session.Editor.ShiftSubtitle(value.Id, value.Start - value.OriginalStart);
        }
        else
        {
            session.Editor.SetSubtitleTiming(value.Id, value.Start, value.End, value.Mode);
        }
    }));
    /// <summary>选择关键帧并同步属性检查器。</summary>
    public void SelectKeyframe(TimelineKeyframeEventArgs value) => session.SelectKeyframe(value);
    /// <summary>提交完成的关键帧手势。</summary>
    public Task MoveKeyframeAsync(TimelineKeyframeEventArgs value) => session.RunCommandAsync(() => session.EditAsync(() => session.MoveKeyframe(value)));
    /// <summary>视口尺寸改变后重新计算滚动范围。</summary>
    public void RefreshViewport() => session.Tick();
}
