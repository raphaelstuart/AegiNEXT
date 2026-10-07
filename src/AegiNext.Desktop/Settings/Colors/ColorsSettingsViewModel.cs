using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.Colors;

/// <summary>主色、音频图与时间轴个人配色草稿，提交后由工作台保存并应用。</summary>
public sealed class ColorsSettingsViewModel : ObservableObject
{
    private bool updating;
    private string accentColor = "#5273E8";
    private AudioGraphPalette audioGraph = new();
    private TimelineClipPalette timelineClips = new();
    private readonly AudioGraphPaletteChoice[] schemes = Enumerable.Range(0, AudioGraphPalettes.CUSTOM_INDEX + 1)
        .Select(index => new AudioGraphPaletteChoice(index)).ToArray();

    /// <summary>从已验证的个人偏好构造颜色输入，不持有控件或工程。</summary>
    public ColorsSettingsViewModel(WorkbenchPreferences preferences)
    {
        ResetColorsCommand = new(ResetColors);
        AccentDraft.Committed += (_, args) =>
        {
            accentColor = ColorHexCodec.Format(args.Value, false);
            AccentDraft.Load(args.Value with { Alpha = 1 });
            OnPropertyChanged(nameof(AccentColor));
            NotifyChanged();
        };
        LowDraft.Committed += (_, args) => CommitPalette(audioGraph with { UseClassicSpectrum = false, Low = ColorHexCodec.Format(args.Value, false) }, LowDraft);
        MidDraft.Committed += (_, args) => CommitPalette(audioGraph with { UseClassicSpectrum = false, Mid = ColorHexCodec.Format(args.Value, false) }, MidDraft);
        HighDraft.Committed += (_, args) => CommitPalette(audioGraph with { UseClassicSpectrum = false, High = ColorHexCodec.Format(args.Value, false) }, HighDraft);
        WaveformDraft.Committed += (_, args) => CommitPalette(audioGraph with { Waveform = ColorHexCodec.Format(args.Value, true) }, WaveformDraft);
        SelectedClipDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { SelectedClip = ColorHexCodec.Format(args.Value, true) }, SelectedClipDraft);
        InactiveClipDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { InactiveClip = ColorHexCodec.Format(args.Value, true) }, InactiveClipDraft);
        StartLineDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { StartLine = ColorHexCodec.Format(args.Value, true) }, StartLineDraft);
        EndLineDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { EndLine = ColorHexCodec.Format(args.Value, true) }, EndLineDraft);
        SelectedRangeFillDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { SelectedRangeFill = ColorHexCodec.Format(args.Value, true) }, SelectedRangeFillDraft);
        InactiveRangeFillDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { InactiveRangeFill = ColorHexCodec.Format(args.Value, true) }, InactiveRangeFillDraft);
        MediaRangeFillDraft.Committed += (_, args) => CommitTimelinePalette(timelineClips with { MediaRangeFill = ColorHexCodec.Format(args.Value, true) }, MediaRangeFillDraft);
        UpdatePreferences(preferences);
    }

    public event EventHandler<SettingsColorsChangedEventArgs>? Changed;
    public RelayCommand ResetColorsCommand { get; }
    public ColorDraft AccentDraft { get; } = new() { IsAlphaEnabled = false };
    public ColorDraft LowDraft { get; } = new() { IsAlphaEnabled = false };
    public ColorDraft MidDraft { get; } = new() { IsAlphaEnabled = false };
    public ColorDraft HighDraft { get; } = new() { IsAlphaEnabled = false };
    public ColorDraft WaveformDraft { get; } = new();
    public ColorDraft SelectedClipDraft { get; } = new();
    public ColorDraft InactiveClipDraft { get; } = new();
    public ColorDraft StartLineDraft { get; } = new();
    public ColorDraft EndLineDraft { get; } = new();
    public ColorDraft SelectedRangeFillDraft { get; } = new();
    public ColorDraft InactiveRangeFillDraft { get; } = new();
    public ColorDraft MediaRangeFillDraft { get; } = new();
    public string AccentColor => accentColor;
    public AudioGraphPalette AudioGraph => audioGraph;
    public TimelineClipPalette TimelineClips => timelineClips;
    public AudioGraphPaletteChoice[] Schemes => schemes;
    public AudioGraphPaletteChoice SelectedScheme
    {
        get => schemes[SchemeIndex];
        set
        {
            if (value is not null && schemes.Contains(value))
            {
                SchemeIndex = value.Index;
            }
        }
    }

    public int SchemeIndex
    {
        get => AudioGraphPalettes.IndexOf(audioGraph);
        set
        {
            if (updating || value == SchemeIndex || value < 0 || value > AudioGraphPalettes.CUSTOM_INDEX)
            {
                return;
            }

            if (new[] { LowDraft, MidDraft, HighDraft, WaveformDraft }.Any(draft => !draft.TryCommit(out _)))
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedScheme));
                return;
            }

            audioGraph = value == AudioGraphPalettes.CUSTOM_INDEX
                ? audioGraph with { UseClassicSpectrum = false, AdaptToTheme = false }
                : AudioGraphPalettes.Get(value);
            LoadPalette(true);
            OnPropertyChanged(nameof(AudioGraph));
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScheme));
            NotifyChanged();
        }
    }

    /// <summary>回填已应用设置并保留未完成输入，不发出用户修改事件。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        updating = true;
        try
        {
            accentColor = value.AccentColor;
            audioGraph = value.AudioGraph;
            timelineClips = value.TimelineClips;
            AccentDraft.Load(Parse(accentColor), false);
            LoadPalette(false);
            LoadTimelinePalette(false);
            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(AudioGraph));
            OnPropertyChanged(nameof(TimelineClips));
            OnPropertyChanged(nameof(SchemeIndex));
            OnPropertyChanged(nameof(SelectedScheme));
            RefreshLanguage();
        }
        finally
        {
            updating = false;
        }
    }

    /// <summary>即时刷新配色名称，保留颜色草稿与表示模式。</summary>
    public void RefreshLanguage()
    {
        var wasUpdating = updating;
        updating = true;
        try
        {
            var keys = new[] { "AudioClassic", "AudioIce", "AudioFire", "AudioGray", "AudioCustom" };
            for (var index = 0; index < schemes.Length; index++)
            {
                schemes[index].Label = Localization.Get("Settings." + keys[index]);
            }
            OnPropertyChanged(nameof(SchemeIndex));
            OnPropertyChanged(nameof(SelectedScheme));
            foreach (var draft in new[]
                     {
                         AccentDraft, LowDraft, MidDraft, HighDraft, WaveformDraft,
                         SelectedClipDraft, InactiveClipDraft, StartLineDraft, EndLineDraft, SelectedRangeFillDraft, InactiveRangeFillDraft,
                         MediaRangeFillDraft
                     })
            {
                draft.RefreshLanguage();
            }
        }
        finally
        {
            updating = wasUpdating;
        }
    }

    private void CommitPalette(AudioGraphPalette value, ColorDraft committed)
    {
        audioGraph = value with { AdaptToTheme = false };
        committed.Load(committed.Value);
        LoadPalette(false);
        OnPropertyChanged(nameof(AudioGraph));
        OnPropertyChanged(nameof(SchemeIndex));
        OnPropertyChanged(nameof(SelectedScheme));
        NotifyChanged();
    }

    private void ResetColors()
    {
        var defaults = new WorkbenchPreferences();
        updating = true;
        try
        {
            accentColor = defaults.AccentColor;
            audioGraph = defaults.AudioGraph;
            timelineClips = defaults.TimelineClips;
            AccentDraft.Load(Parse(accentColor));
            LoadPalette(true);
            LoadTimelinePalette(true);
            OnPropertyChanged(nameof(AccentColor));
            OnPropertyChanged(nameof(AudioGraph));
            OnPropertyChanged(nameof(TimelineClips));
            OnPropertyChanged(nameof(SchemeIndex));
            OnPropertyChanged(nameof(SelectedScheme));
        }
        finally
        {
            updating = false;
        }

        NotifyChanged();
    }

    private void LoadPalette(bool discardDrafts)
    {
        LowDraft.Load(Parse(audioGraph.Low), discardDrafts);
        MidDraft.Load(Parse(audioGraph.Mid), discardDrafts);
        HighDraft.Load(Parse(audioGraph.High), discardDrafts);
        WaveformDraft.Load(Parse(audioGraph.Waveform), discardDrafts);
    }

    private void CommitTimelinePalette(TimelineClipPalette value, ColorDraft committed)
    {
        timelineClips = value with { AdaptToTheme = false };
        committed.Load(committed.Value);
        LoadTimelinePalette(false);
        OnPropertyChanged(nameof(TimelineClips));
        NotifyChanged();
    }

    private void LoadTimelinePalette(bool discardDrafts)
    {
        SelectedClipDraft.Load(Parse(timelineClips.SelectedClip), discardDrafts);
        InactiveClipDraft.Load(Parse(timelineClips.InactiveClip), discardDrafts);
        StartLineDraft.Load(Parse(timelineClips.StartLine), discardDrafts);
        EndLineDraft.Load(Parse(timelineClips.EndLine), discardDrafts);
        SelectedRangeFillDraft.Load(Parse(timelineClips.SelectedRangeFill), discardDrafts);
        InactiveRangeFillDraft.Load(Parse(timelineClips.InactiveRangeFill), discardDrafts);
        MediaRangeFillDraft.Load(Parse(timelineClips.MediaRangeFill), discardDrafts);
    }

    private void NotifyChanged()
    {
        if (!updating)
        {
            audioGraph.Validate();
            timelineClips.Validate();
            Changed?.Invoke(this, new(accentColor, audioGraph, timelineClips));
        }
    }

    private static SceneColor Parse(string value)
    {
        if (!ColorHexCodec.TryParse(value, 1, true, out var color))
        {
            throw new InvalidDataException("配色值无效。");
        }

        return color;
    }
}
