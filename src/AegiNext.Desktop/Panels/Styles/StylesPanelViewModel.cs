using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Media;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class StylesPanelViewModel : ObservableObject
{
    private string fontSizeText = "64";
    private string strokeWidthText = "2";
    private readonly WorkbenchSession session;
    private string fontFamily = "Noto Sans CJK SC";
    private string fontDraft = "Noto Sans CJK SC";
    private SubtitleFontVariant? fontVariant;
    private bool fontSelectionCommitted;
    private bool synchronizingFont;
    private bool currentSystemFont = true;
    private decimal? fontSize = 64;
    private decimal? strokeWidth = 2;
    private bool? bold = false;
    private bool? italic = false;
    private int alignment;
    private string[] alignments = [];
    private bool refreshingAlignmentChoices;
    private bool hasCue;
    private StylePresetListItem[] presets = [];
    private StylePresetListItem? selectedPreset;
    private bool canApplyPreset;
    internal event EventHandler? AlignmentChoicesRefreshing;
    internal event EventHandler? AlignmentChoicesRefreshed;

    internal StylesPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        FillDraft.Changed += (_, _) => OnPropertyChanged(nameof(FillDraft));
        StrokeDraft.Changed += (_, _) => OnPropertyChanged(nameof(StrokeDraft));
        Position.Changed += (_, _) => OnPropertyChanged(nameof(Position));
        ApplyStyleCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.ApplySelectedStyleAsync()));
        ManageStylesCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.RequestSettingsAsync(SettingsPage.STYLES)));
        RestoreAutomaticPositionCommand = new AsyncRelayCommand(RestoreAutomaticPositionAsync);
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

    public SubtitleFontVariant? FontVariant
    {
        get => fontVariant;
        set => SetProperty(ref fontVariant, value);
    }

    public bool FontSelectionCommitted
    {
        get => fontSelectionCommitted;
        private set => SetProperty(ref fontSelectionCommitted, value);
    }

    internal FontSelection CurrentFont => new(FontFamily, FontVariant, currentSystemFont);
    internal SubtitleFontSelectionService Fonts => session.Fonts;

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
        set
        {
            if (!refreshingAlignmentChoices)
            {
                SetProperty(ref alignment, value);
            }
        }
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

    /// <summary>确认字体输入并通过统一事务提交。</summary>
    public void CommitFont(string family)
    {
        CommitFont(new FontSelection(family));
    }

    internal void CommitFont(FontSelection selection)
    {
        if (session.IsUpdating)
        {
            return;
        }
        synchronizingFont = true;
        try
        {
            FontFamily = selection.FamilyName;
            FontVariant = selection.Variant;
            currentSystemFont = selection.IsSystemFont;
            FontDraft = selection.DisplayName;
            if (selection.Variant is { } variant)
            {
                Bold = variant.Weight >= 700;
                Italic = variant.Italic;
            }
            FontSelectionCommitted = true;
            OnPropertyChanged(nameof(CurrentFont));
        }
        finally
        {
            synchronizingFont = false;
        }
        session.TryCommitDrafts();
    }

    internal void LoadFont(SubtitleStyle style)
    {
        synchronizingFont = true;
        try
        {
            FontFamily = style.FontFamily;
            FontVariant = style.FontVariant;
            currentSystemFont = !style.FontAssetId.HasValue;
            FontDraft = CurrentFont.DisplayName;
            Bold = style.Bold;
            Italic = style.Italic;
            FontSelectionCommitted = false;
            OnPropertyChanged(nameof(CurrentFont));
        }
        finally
        {
            synchronizingFont = false;
        }
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
        if (synchronizingFont || session.IsUpdating)
        {
            return;
        }
        if (TryReadFontStyle(out var style))
        {
            SetFormatting(Fonts.ToggleBold(style, value), style);
        }
        session.TryCommitDrafts();
    }
    /// <summary>提交用户切换后的斜体状态。</summary>
    public void CommitItalic(bool value)
    {
        if (synchronizingFont || session.IsUpdating)
        {
            return;
        }
        if (TryReadFontStyle(out var style))
        {
            SetFormatting(Fonts.ToggleItalic(style, value), style);
        }
        session.TryCommitDrafts();
    }

    private bool TryReadFontStyle(out SubtitleStyle style)
    {
        var source = session.SelectedCue?.Style ?? new();
        if (!FontSelectionResolver.TryResolve(Fonts.Candidates, CurrentFont, FontDraft, out var selection))
        {
            style = source;
            return false;
        }
        var parsesDraft = selection.FamilyName != FontFamily || selection.Variant != FontVariant;
        var fontChanged = selection.FamilyName != source.FontFamily || selection.Variant != source.FontVariant || FontSelectionCommitted;
        style = source with
        {
            FontFamily = selection.FamilyName, FontVariant = selection.Variant,
            Bold = parsesDraft && selection.Variant is { } variant ? variant.Weight >= 700 : Bold == true,
            Italic = parsesDraft && selection.Variant is { } italicVariant ? italicVariant.Italic : Italic == true,
            FontAssetId = fontChanged ? null : source.FontAssetId
        };
        return true;
    }

    private void SetFormatting(SubtitleInlineStyleOverride edit, SubtitleStyle source)
    {
        var style = edit.ApplyTo(source);
        synchronizingFont = true;
        try
        {
            FontFamily = style.FontFamily;
            FontVariant = style.FontVariant;
            currentSystemFont = !style.FontAssetId.HasValue;
            Bold = style.Bold;
            Italic = style.Italic;
            FontDraft = CurrentFont.DisplayName;
            OnPropertyChanged(nameof(CurrentFont));
        }
        finally
        {
            synchronizingFont = false;
        }
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
        if (refreshingAlignmentChoices)
        {
            return;
        }
        Alignment = value;
        session.TryCommitDrafts();
    }

    internal void RefreshAlignmentChoices(string[] options)
    {
        var selection = alignment;
        refreshingAlignmentChoices = true;
        try
        {
            AlignmentChoicesRefreshing?.Invoke(this, EventArgs.Empty);
            Alignments = options;
            alignment = selection;
            OnPropertyChanged(nameof(Alignment));
        }
        finally
        {
            try
            {
                AlignmentChoicesRefreshed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                refreshingAlignmentChoices = false;
            }
        }
    }
}
