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

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class StylesPanelViewModel : ObservableObject
{
    private string fontSizeText = "64";
    private string strokeWidthText = "2";
    private readonly WorkbenchSession session;
    private string fontFamily = "Noto Sans CJK SC";
    private string fontDraft = "Noto Sans CJK SC";
    private decimal? fontSize = 64;
    private decimal? strokeWidth = 2;
    private bool? bold = false;
    private bool? italic = false;
    private int alignment;
    private string[] alignments = [];
    private bool hasCue;
    private StylePresetListItem[] presets = [];
    private StylePresetListItem? selectedPreset;
    private bool canApplyPreset;

    internal StylesPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        FillDraft.Changed += (_, _) => OnPropertyChanged(nameof(FillDraft));
        StrokeDraft.Changed += (_, _) => OnPropertyChanged(nameof(StrokeDraft));
        Position.Changed += (_, _) => OnPropertyChanged(nameof(Position));
        ApplyStyleCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.ApplySelectedStyleAsync()));
        ManageStylesCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.RequestSettingsAsync(AegiNext.Desktop.Settings.SettingsPage.STYLES)));
        RestoreAutomaticPositionCommand = new AsyncRelayCommand(RestoreAutomaticPositionAsync);
        KaraokeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.CreateKaraoke)));
        ClearKaraokeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.ClearKaraoke)));
    }

    public string FontFamily
    {
        get => fontFamily;
        set => SetProperty(ref fontFamily, value);
    }

    public string FontDraft
    {
        get => fontDraft;
        set => SetProperty(ref fontDraft, value);
    }

    public decimal? FontSize
    {
        get => fontSize;
        set => SetProperty(ref fontSize, value);
    }

    public decimal? StrokeWidth
    {
        get => strokeWidth;
        set => SetProperty(ref strokeWidth, value);
    }

    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);

    public Color Fill
    {
        get => SceneColorConversion.ToColor(FillDraft.Value);
        set
        {
            if (value != Fill)
            {
                FillDraft.SetValue(SceneColorConversion.FromColor(value));
            }
        }
    }

    public Color Stroke
    {
        get => SceneColorConversion.ToColor(StrokeDraft.Value);
        set
        {
            if (value != Stroke)
            {
                StrokeDraft.SetValue(SceneColorConversion.FromColor(value));
            }
        }
    }

    public bool? Bold
    {
        get => bold;
        set => SetProperty(ref bold, value);
    }

    public bool? Italic
    {
        get => italic;
        set => SetProperty(ref italic, value);
    }

    public int Alignment
    {
        get => alignment;
        set => SetProperty(ref alignment, value);
    }

    public string[] Alignments
    {
        get => alignments;
        set => SetProperty(ref alignments, value);
    }

    public bool HasCue
    {
        get => hasCue;
        set => SetProperty(ref hasCue, value);
    }

    public StylePresetListItem[] Presets
    {
        get => presets;
        set => SetProperty(ref presets, value);
    }

    public StylePresetListItem? SelectedPreset
    {
        get => selectedPreset;
        set => SetProperty(ref selectedPreset, value);
    }

    public bool CanApplyPreset
    {
        get => canApplyPreset;
        set => SetProperty(ref canApplyPreset, value);
    }

    /// <summary>通过会话事务恢复当前字幕的自动对齐与位置。</summary>
    public Task RestoreAutomaticPositionAsync() => session.RunCommandAsync(() => session.EditAsync(session.ResetAutomaticPosition));

    public ICommand ApplyStyleCommand { get; }
    public SubtitlePositionDraft Position { get; } = new();

    public ICommand ManageStylesCommand { get; }

    public ICommand RestoreAutomaticPositionCommand { get; }

    public ICommand KaraokeCommand { get; }

    public ICommand ClearKaraokeCommand { get; }
    /// <summary>确认字体输入并通过统一事务提交。</summary>
    public void CommitFont(string family)
    {
        FontFamily = family;
        FontDraft = family;
        session.TryCommitDrafts();
    }
    /// <summary>提交所有面板的有效草稿。</summary>
    public void CommitDrafts() => session.TryCommitDrafts(false);
    public string FontSizeText
    {
        get => fontSizeText;
        set => SetProperty(ref fontSizeText, value);
    }
    public string StrokeWidthText
    {
        get => strokeWidthText;
        set => SetProperty(ref strokeWidthText, value);
    }
    /// <summary>提交用户切换后的粗体状态。</summary>
    public void CommitBold(bool value)
    {
        Bold = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户切换后的斜体状态。</summary>
    public void CommitItalic(bool value)
    {
        Italic = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的填充颜色。</summary>
    public void CommitFill(Color value)
    {
        Fill = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的描边颜色。</summary>
    public void CommitStroke(Color value)
    {
        Stroke = value;
        session.TryCommitDrafts();
    }
    /// <summary>提交用户选择的对齐方式。</summary>
    public void CommitAlignment(int value)
    {
        Alignment = value;
        session.TryCommitDrafts();
    }
}
