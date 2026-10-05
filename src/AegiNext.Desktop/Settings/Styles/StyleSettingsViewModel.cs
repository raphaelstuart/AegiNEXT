using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Settings.Styles;

/// <summary>字幕样式库页面的独立草稿、验证和语义请求。</summary>
public sealed class StyleSettingsViewModel : ObservableObject
{
    private ImmutableArray<SubtitleStylePreset> styles = [];
    private SettingsStyleDraft? draft;
    private SubtitleStylePreset? selectedStyle;
    private string name = string.Empty;
    private string? error;
    private string? errorKey;
    private bool busy;
    private bool hasSelectedSubtitle;
    private bool loading;
    private int draftVersion;
    private string[] alignments = [];
    private decimal? fontSize;
    private decimal? strokeWidth;
    private decimal? margin;
    private decimal? lineHeight;
    private decimal? shadowBlur;
    private decimal? shadowX;
    private decimal? shadowY;
    private string fontSizeText = string.Empty;
    private string strokeWidthText = string.Empty;
    private string marginText = string.Empty;
    private string lineHeightText = string.Empty;
    private string shadowBlurText = string.Empty;
    private string shadowXText = string.Empty;
    private string shadowYText = string.Empty;
    private string? invalidFieldKey;
    private Func<SubtitleStylePreset, SubtitlePositionMeasurement>? measurePosition;
    private string? positionMeasurementError;

    /// <summary>创建样式命令；工程和存储操作交由会话处理。</summary>
    public StyleSettingsViewModel()
    {
        FillDraft.Committed += (_, args) => Fill = args.Value;
        StrokeDraft.Committed += (_, args) => Stroke = args.Value;
        ShadowDraft.Committed += (_, args) => ShadowColor = args.Value;
        Position.Changed += (_, _) =>
        {
            if (Position.Validate() is null)
            {
                ChangeStyle(style => style with { Position = Position.CreatePosition() });
            }
        };
        AddCommand = new(Add, () => !IsBusy);
        DuplicateCommand = new(Duplicate, () => HasDraft && !IsBusy);
        DeleteCommand = new(() => DeleteRequested?.Invoke(this, new(draft!.Preset.Id)), () => CanDelete);
        SaveCommand = new(() => Submit(false), () => HasDraft && !IsBusy);
        ApplyCommand = new(() => Submit(true), () => HasDraft && HasSelectedSubtitle && !IsBusy);
        CaptureCommand = new(() => CaptureRequested?.Invoke(this, EventArgs.Empty),
            () => HasSelectedSubtitle && !IsBusy);
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        ExportCommand = new(() => ExportRequested?.Invoke(this, EventArgs.Empty), () => !IsEmpty && !IsBusy);
        RefreshLanguage();
    }

    public event EventHandler<SettingsStyleEventArgs>? UpsertRequested;
    public event EventHandler<SettingsStyleEventArgs>? ApplyRequested;
    public event EventHandler<SettingsStyleDeleteEventArgs>? DeleteRequested;
    public event EventHandler? CaptureRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;
    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand CaptureCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public ImmutableArray<SubtitleStylePreset> Styles => styles;
    public SubtitlePositionDraft Position { get; } = new();
    public ColorDraft FillDraft { get; } = new();
    public ColorDraft StrokeDraft { get; } = new(SceneColor.Black);
    public ColorDraft ShadowDraft { get; } = new(SceneColor.Black);
    public string? PositionMeasurementError => positionMeasurementError;
    public bool HasPositionMeasurementError => positionMeasurementError is not null;
    public SubtitleStylePreset? Draft => draft?.Preset;
    public bool HasDraft => draft is not null;
    public bool IsEmpty => styles.IsEmpty;
    public bool CanDelete => HasDraft && !IsBusy && styles.Any(value => value.Id == draft!.Preset.Id);
    public bool CanEdit => HasDraft && !IsBusy;
    public bool CanApply => CanEdit && HasSelectedSubtitle;
    public bool IsAvailable => !IsBusy;
    public string FontSource => Localization.Get("Settings." + (draft?.Preset.Font is null ? "SystemFont" : "EmbeddedFont"));
    public string[] Alignments => alignments;
    public int DraftVersion => draftVersion;

    public string? InvalidFieldKey
    {
        get => invalidFieldKey;
        private set => SetProperty(ref invalidFieldKey, value);
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    public bool IsBusy
    {
        get => busy;
        set
        {
            if (SetProperty(ref busy, value))
            {
                RefreshActions();
            }
        }
    }

    public bool HasSelectedSubtitle
    {
        get => hasSelectedSubtitle;
        set
        {
            if (SetProperty(ref hasSelectedSubtitle, value))
            {
                RefreshActions();
            }
        }
    }

    public SubtitleStylePreset? SelectedStyle
    {
        get => selectedStyle;
        set
        {
            if (SetProperty(ref selectedStyle, value) && !loading)
            {
                LoadDraft(value is null ? null : new(value));
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value) && !loading)
            {
                draft?.Rename(value);
            }
        }
    }

    public decimal? FontSize
    {
        get => fontSize;
        set => SetNumericValue(ref fontSize, value, nameof(FontSize),
            (style, number) => style with { FontSize = (double)number });
    }

    public string FontSizeText
    {
        get => fontSizeText;
        set => SetNumericText(ref fontSizeText, value, nameof(FontSizeText), number => FontSize = number);
    }

    public decimal? StrokeWidth
    {
        get => strokeWidth;
        set => SetNumericValue(ref strokeWidth, value, nameof(StrokeWidth),
            (style, number) => style with { StrokeWidth = (double)number });
    }

    public string StrokeWidthText
    {
        get => strokeWidthText;
        set => SetNumericText(ref strokeWidthText, value, nameof(StrokeWidthText), number => StrokeWidth = number);
    }

    public decimal? Margin
    {
        get => margin;
        set => SetNumericValue(ref margin, value, nameof(Margin),
            (style, number) => style with { Margin = (double)number });
    }

    public string MarginText
    {
        get => marginText;
        set => SetNumericText(ref marginText, value, nameof(MarginText), number => Margin = number);
    }

    public decimal? LineHeight
    {
        get => lineHeight;
        set => SetNumericValue(ref lineHeight, value, nameof(LineHeight),
            (style, number) => style with { LineHeight = (double)number });
    }

    public string LineHeightText
    {
        get => lineHeightText;
        set => SetNumericText(ref lineHeightText, value, nameof(LineHeightText), number => LineHeight = number);
    }

    public decimal? ShadowBlur
    {
        get => shadowBlur;
        set => SetNumericValue(ref shadowBlur, value, nameof(ShadowBlur),
            (style, number) => style with { ShadowBlur = (double)number });
    }

    public string ShadowBlurText
    {
        get => shadowBlurText;
        set => SetNumericText(ref shadowBlurText, value, nameof(ShadowBlurText), number => ShadowBlur = number);
    }

    public decimal? ShadowX
    {
        get => shadowX;
        set => SetNumericValue(ref shadowX, value, nameof(ShadowX),
            (style, number) => style with { ShadowOffset = new((double)number, style.ShadowOffset.Y) });
    }

    public string ShadowXText
    {
        get => shadowXText;
        set => SetNumericText(ref shadowXText, value, nameof(ShadowXText), number => ShadowX = number);
    }

    public decimal? ShadowY
    {
        get => shadowY;
        set => SetNumericValue(ref shadowY, value, nameof(ShadowY),
            (style, number) => style with { ShadowOffset = new(style.ShadowOffset.X, (double)number) });
    }

    public string ShadowYText
    {
        get => shadowYText;
        set => SetNumericText(ref shadowYText, value, nameof(ShadowYText), number => ShadowY = number);
    }

    public bool Bold
    {
        get => draft?.Preset.Style.Bold ?? false;
        set
        {
            if (!EqualityComparer<bool>.Default.Equals(Bold, value))
            {
                ChangeStyle(style => style with { Bold = value });
                OnPropertyChanged();
            }
        }
    }

    public bool Italic
    {
        get => draft?.Preset.Style.Italic ?? false;
        set
        {
            if (!EqualityComparer<bool>.Default.Equals(Italic, value))
            {
                ChangeStyle(style => style with { Italic = value });
                OnPropertyChanged();
            }
        }
    }

    public SceneColor Fill
    {
        get => draft?.Preset.Style.Fill ?? new();
        set
        {
            if (!EqualityComparer<SceneColor>.Default.Equals(Fill, value))
            {
                ChangeStyle(style => style with { Fill = value });
                OnPropertyChanged();
            }
            FillDraft.Load(Fill);
        }
    }

    public SceneColor Stroke
    {
        get => draft?.Preset.Style.Stroke ?? new();
        set
        {
            if (!EqualityComparer<SceneColor>.Default.Equals(Stroke, value))
            {
                ChangeStyle(style => style with { Stroke = value });
                OnPropertyChanged();
            }
            StrokeDraft.Load(Stroke);
        }
    }

    public SceneColor ShadowColor
    {
        get => draft?.Preset.Style.ShadowColor ?? new();
        set
        {
            if (!EqualityComparer<SceneColor>.Default.Equals(ShadowColor, value))
            {
                ChangeStyle(style => style with { ShadowColor = value });
                OnPropertyChanged();
            }
            ShadowDraft.Load(ShadowColor);
        }
    }

    public int AlignmentIndex
    {
        get => (int)(draft?.Preset.Style.Alignment ?? ProjectTextAlignment.BOTTOM_CENTER);
        set
        {
            if (Enum.IsDefined((ProjectTextAlignment)value) && AlignmentIndex != value)
            {
                ChangeStyle(style => style with { Alignment = (ProjectTextAlignment)value });
                OnPropertyChanged();
            }
        }
    }

    /// <summary>同步已提交样式库并保留选择标识。</summary>
    public void UpdateStyles(IEnumerable<SubtitleStylePreset> presets, Guid? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var selection = selectedId ?? draft?.Preset.Id;
        styles = presets.ToImmutableArray();
        OnPropertyChanged(nameof(Styles));
        loading = true;
        SelectedStyle = styles.FirstOrDefault(value => value.Id == selection) ?? styles.FirstOrDefault();
        loading = false;
        LoadDraft(SelectedStyle is null ? null : new(SelectedStyle));
        errorKey = null;
        Error = null;
    }

    /// <summary>注入组合根提供的纯测量入口；页面模型不拥有字体或原生渲染器。</summary>
    public void SetPositionMeasurement(Func<SubtitleStylePreset, SubtitlePositionMeasurement> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        measurePosition = measure;
        RefreshPositionMeasurement(true);
    }

    /// <summary>提交局部字体控件已确认的字体名。</summary>
    public void CommitFont(string familyName)
    {
        ChangeStyle(style => style with { FontFamily = familyName });
    }

    /// <summary>显示字体控件拒绝空白输入的结果。</summary>
    public void RejectFont()
    {
        InvalidFieldKey = "FontInput";
        SetError("FontRequired");
    }

    /// <summary>刷新标签但不覆盖尚未确认的草稿。</summary>
    public void RefreshLanguage()
    {
        var wasLoading = loading;
        loading = true;
        try
        {
            alignments = Enum.GetValues<ProjectTextAlignment>().Select(value => Localization.Get("Settings." + value.ToString()))
                .ToArray();
            OnPropertyChanged(nameof(Alignments));
            OnPropertyChanged(nameof(AlignmentIndex));
            OnPropertyChanged(nameof(FontSource));
            FillDraft.RefreshLanguage();
            StrokeDraft.RefreshLanguage();
            ShadowDraft.RefreshLanguage();
            if (errorKey is not null)
            {
                Error = Localization.Get("Settings." + errorKey);
            }
        }
        finally
        {
            loading = wasLoading;
        }
    }

    private void Add()
    {
        ClearSelection();
        LoadDraft(new(new(Guid.NewGuid(), SettingsStyleDraft.UniqueName(Localization.Get("Settings.NewStyle"), styles), new())));
    }

    private void Duplicate()
    {
        if (draft is null)
        {
            return;
        }

        var next = draft.Duplicate(
            SettingsStyleDraft.UniqueName($"{draft.Preset.Name} {Localization.Get("Settings.CopySuffix")}", styles));
        ClearSelection();
        LoadDraft(next);
    }

    private void ClearSelection()
    {
        loading = true;
        SelectedStyle = null;
        loading = false;
    }

    private void ChangeStyle(Func<SubtitleStyle, SubtitleStyle> change)
    {
        if (!loading && draft is not null)
        {
            draft.UpdateStyle(change(draft.Preset.Style));
            RefreshPositionMeasurement(!Position.IsExplicit);
            OnPropertyChanged(nameof(FontSource));
        }
    }

    private void SetNumericValue(ref decimal? field, decimal? value, string property,
        Func<SubtitleStyle, decimal, SubtitleStyle> change)
    {
        if (SetProperty(ref field, value, property) && value is { } number)
        {
            ChangeStyle(style => change(style, number));
        }
    }

    private void SetNumericText(ref string field, string value, string property, Action<decimal> change)
    {
        if (SetProperty(ref field, value, property))
        {
            if (ParseNumber(value) is { } number)
            {
                change(number);
            }
        }
    }

    private static decimal? ParseNumber(string value)
    {
        return decimal.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ? number : null;
    }

    private static string FormatNumber(decimal? value)
    {
        return value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private void LoadDraft(SettingsStyleDraft? next)
    {
        draft = next;
        loading = true;
        try
        {
            var style = draft?.Preset.Style ?? new();
            Name = draft?.Preset.Name ?? string.Empty;
            FontSize = (decimal)style.FontSize;
            StrokeWidth = (decimal)style.StrokeWidth;
            Margin = (decimal)style.Margin;
            LineHeight = (decimal)style.LineHeight;
            ShadowBlur = (decimal)style.ShadowBlur;
            ShadowX = (decimal)style.ShadowOffset.X;
            ShadowY = (decimal)style.ShadowOffset.Y;
            FontSizeText = FormatNumber(FontSize);
            StrokeWidthText = FormatNumber(StrokeWidth);
            MarginText = FormatNumber(Margin);
            LineHeightText = FormatNumber(LineHeight);
            ShadowBlurText = FormatNumber(ShadowBlur);
            ShadowXText = FormatNumber(ShadowX);
            ShadowYText = FormatNumber(ShadowY);
            FillDraft.Load(style.Fill);
            StrokeDraft.Load(style.Stroke);
            ShadowDraft.Load(style.ShadowColor);
            RefreshPositionMeasurement(true);
            foreach (var property in new[]
                     {
                         nameof(Bold), nameof(Italic), nameof(Fill), nameof(Stroke), nameof(ShadowColor),
                         nameof(AlignmentIndex), nameof(FontSource), nameof(Draft)
                     })
            {
                OnPropertyChanged(property);
            }

            draftVersion++;
            OnPropertyChanged(nameof(DraftVersion));
        }
        finally
        {
            loading = false;
        }

        errorKey = null;
        Error = null;
        InvalidFieldKey = null;
        RefreshActions();
    }

    private void RefreshPositionMeasurement(bool reload)
    {
        var measurement = draft is not null ? measurePosition?.Invoke(draft.Preset) : null;
        positionMeasurementError = measurement?.Error;
        OnPropertyChanged(nameof(PositionMeasurementError));
        OnPropertyChanged(nameof(HasPositionMeasurementError));
        if (reload)
        {
            Position.Load(draft?.Preset.Style ?? new(), measurement?.Position, true, measurement?.Geometry);
        }
        else
        {
            Position.UpdateGeometry(measurement?.Geometry);
        }
    }

    private void RefreshActions()
    {
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanDelete));
        foreach (var command in new[]
                 {
                     AddCommand, DuplicateCommand, DeleteCommand, SaveCommand, ApplyCommand, CaptureCommand,
                     ImportCommand, ExportCommand
                 })
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private void Submit(bool apply)
    {
        if (draft is null)
        {
            return;
        }

        var preset = draft.Preset;
        foreach (var color in new (ColorDraft Draft, string Key)[]
                 { (FillDraft, "FillPicker"), (StrokeDraft, "StrokePicker"), (ShadowDraft, "ShadowPicker") })
        {
            if (!color.Draft.TryCommit(out _))
            {
                InvalidFieldKey = color.Key + "." + color.Draft.InvalidFieldKey;
                SetError("StyleValidation");
                return;
            }
        }
        _ = FillDraft.TryCommit(out var fillColor);
        _ = StrokeDraft.TryCommit(out var strokeColor);
        _ = ShadowDraft.TryCommit(out var shadowColorValue);
        preset = preset with { Style = preset.Style with { Fill = fillColor, Stroke = strokeColor, ShadowColor = shadowColorValue } };
        if (Position.Validate() is { } positionKey)
        {
            InvalidFieldKey = positionKey;
            SetError("StyleValidation");
            return;
        }
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            InvalidFieldKey = "StyleNameInput";
            SetError("NameRequired");
            return;
        }

        if (!apply && styles.Any(value =>
                value.Id != preset.Id && string.Equals(value.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
        {
            InvalidFieldKey = "StyleNameInput";
            SetError("DuplicateName");
            return;
        }

        if (string.IsNullOrWhiteSpace(preset.Style.FontFamily))
        {
            RejectFont();
            return;
        }

        foreach (var field in new (decimal? Value, decimal Minimum, decimal Maximum, string Key)[]
                 {
                     (FontSize is null ? null : ParseNumber(FontSizeText), 0.01m, 4096, "FontSizeInput"),
                     (StrokeWidth is null ? null : ParseNumber(StrokeWidthText), 0, 4096, "StrokeWidthInput"),
                     (Margin is null ? null : ParseNumber(MarginText), 0, 32768, "MarginInput"),
                     (LineHeight is null ? null : ParseNumber(LineHeightText), 0.1m, 10, "LineHeightInput"),
                     (ShadowBlur is null ? null : ParseNumber(ShadowBlurText), 0, 512, "ShadowBlurInput"),
                     (ShadowX is null ? null : ParseNumber(ShadowXText), -1000000000, 1000000000, "ShadowXInput"),
                     (ShadowY is null ? null : ParseNumber(ShadowYText), -1000000000, 1000000000, "ShadowYInput")
                 })
        {
            if (field.Value is null || field.Value < field.Minimum || field.Value > field.Maximum)
            {
                InvalidFieldKey = field.Key;
                SetError("StyleValidation");
                return;
            }
        }

        try
        {
            SubtitleStylePresetValidator.Validate(preset);
        }
        catch (InvalidDataException)
        {
            SetError("StyleValidation");
            return;
        }

        errorKey = null;
        Error = null;
        InvalidFieldKey = null;
        draft.UpdateStyle(preset.Style);
        FillDraft.Load(fillColor);
        StrokeDraft.Load(strokeColor);
        ShadowDraft.Load(shadowColorValue);
        if (apply)
        {
            ApplyRequested?.Invoke(this, new(preset));
        }
        else
        {
            UpsertRequested?.Invoke(this, new(preset));
        }
    }

    private void SetError(string key)
    {
        errorKey = key;
        Error = Localization.Get("Settings." + key);
    }
}
