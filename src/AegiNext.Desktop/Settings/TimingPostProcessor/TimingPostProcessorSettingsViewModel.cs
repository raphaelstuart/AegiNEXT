using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using AegiNext.Core.Timing;
using AegiNext.Core.Presets;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>管理样式关联目标和处理参数草稿，由应用协调器保存样式库。</summary>
public sealed class TimingPostProcessorSettingsViewModel : ObservableObject
{
    private readonly Dictionary<TimingPostProcessorField, string> drafts = [];
    private readonly HashSet<TimingPostProcessorField> invalidFields = [];
    private TimingPostProcessorPreferences preferences;
    private TimingStyleChoice? selectedStyle;
    private bool isBusy;
    private bool refreshingStyles;
    private string? statusKey;
    private int changedCount;

    /// <summary>以共享偏好初始化参数和独立的毫秒输入草稿。</summary>
    public TimingPostProcessorSettingsViewModel(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        preferences = value.TimingPostProcessor;
        preferences.Validate();
        foreach (var field in Enum.GetValues<TimingPostProcessorField>())
        {
            drafts[field] = Format(ReadValue(Options, field));
        }

        AssociateCommand = new(RequestAssociation, () => CanAssociate);
        UnlinkCommand = new(RequestUnlink, () => CanAssociate);
        SelectAllCommand = new(() => SelectStyles(true), () => !IsBusy);
        SelectNoneCommand = new(() => SelectStyles(false), () => !IsBusy);
    }

    public event EventHandler<TimingPostProcessorPreferencesChangedEventArgs>? Changed;
    public event EventHandler<TimingPostProcessorAssociationEventArgs>? AssociateRequested;
    public event EventHandler<TimingPostProcessorAssociationEventArgs>? UnlinkRequested;
    public ObservableCollection<TimingStyleChoice> Styles { get; } = [];
    public RelayCommand AssociateCommand { get; }
    public RelayCommand UnlinkCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public TimingPostProcessorOptions Options => preferences.Options;
    public bool CanAssociate => !IsBusy && Styles.Any(style => style.IsSelected);
    public TimingStyleChoice? SelectedStyle
    {
        get => selectedStyle;
        set
        {
            if (refreshingStyles || value == selectedStyle || IsBusy || !CommitAll())
            {
                return;
            }

            if (SetProperty(ref selectedStyle, value) && value?.Options is { } options)
            {
                ApplyPreferences(preferences with { Options = options });
                foreach (var timingField in Enum.GetValues<TimingPostProcessorField>())
                {
                    Restore(timingField);
                }
            }
        }
    }
    public string? Error => invalidFields.Count > 0 ? Localization.Get("Settings.TimingMillisecondsInvalid") : null;
    public string? Status => statusKey is null ? null : statusKey is "Settings.TimingAssociated" or "Settings.TimingUnlinked"
        ? Localization.Format(statusKey, changedCount) : Localization.Get(statusKey);

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                statusKey = value ? "Settings.TimingSavingAssociation" : null;
                OnPropertyChanged(nameof(Status));
                RefreshCommands();
            }
        }
    }

    public double BiasPercent
    {
        get => Options.BiasPercent;
        set
        {
            if (double.IsFinite(value) && value is >= 0 and <= 100)
            {
                ApplyPreferences(preferences with { Options = Options with { BiasPercent = (int)Math.Round(value, MidpointRounding.AwayFromZero) } });
            }
        }
    }

    public bool LeadInEnabled
    {
        get => Options.LeadInEnabled;
        set => ApplyPreferences(preferences with { Options = Options with { LeadInEnabled = value } });
    }

    public bool LeadOutEnabled
    {
        get => Options.LeadOutEnabled;
        set => ApplyPreferences(preferences with { Options = Options with { LeadOutEnabled = value } });
    }

    public bool AdjacencyEnabled
    {
        get => Options.AdjacencyEnabled;
        set => ApplyPreferences(preferences with { Options = Options with { AdjacencyEnabled = value } });
    }

    public bool KeyframeSnapEnabled
    {
        get => Options.KeyframeSnapEnabled;
        set => ApplyPreferences(preferences with { Options = Options with { KeyframeSnapEnabled = value } });
    }

    public string LeadInMillisecondsText
    {
        get => drafts[TimingPostProcessorField.LEAD_IN];
        set => SetDraft(TimingPostProcessorField.LEAD_IN, value);
    }

    public string LeadOutMillisecondsText
    {
        get => drafts[TimingPostProcessorField.LEAD_OUT];
        set => SetDraft(TimingPostProcessorField.LEAD_OUT, value);
    }

    public string MaximumGapMillisecondsText
    {
        get => drafts[TimingPostProcessorField.MAXIMUM_GAP];
        set => SetDraft(TimingPostProcessorField.MAXIMUM_GAP, value);
    }

    public string MaximumOverlapMillisecondsText
    {
        get => drafts[TimingPostProcessorField.MAXIMUM_OVERLAP];
        set => SetDraft(TimingPostProcessorField.MAXIMUM_OVERLAP, value);
    }

    public string StartBeforeMillisecondsText
    {
        get => drafts[TimingPostProcessorField.START_BEFORE];
        set => SetDraft(TimingPostProcessorField.START_BEFORE, value);
    }

    public string StartAfterMillisecondsText
    {
        get => drafts[TimingPostProcessorField.START_AFTER];
        set => SetDraft(TimingPostProcessorField.START_AFTER, value);
    }

    public string EndBeforeMillisecondsText
    {
        get => drafts[TimingPostProcessorField.END_BEFORE];
        set => SetDraft(TimingPostProcessorField.END_BEFORE, value);
    }

    public string EndAfterMillisecondsText
    {
        get => drafts[TimingPostProcessorField.END_AFTER];
        set => SetDraft(TimingPostProcessorField.END_AFTER, value);
    }

    /// <summary>仅提交指定字段；非法文本保留原文且不改变已确认参数。</summary>
    public bool Commit(TimingPostProcessorField field)
    {
        if (!TryRead(field, out var milliseconds))
        {
            return false;
        }

        ApplyPreferences(preferences with { Options = WriteValue(Options, field, milliseconds) });
        Restore(field);
        return true;
    }

    /// <summary>同时验证全部原始草稿，全部有效才提交参数快照。</summary>
    public bool CommitAll()
    {
        var next = Options;
        var valid = true;
        foreach (var field in Enum.GetValues<TimingPostProcessorField>())
        {
            if (TryRead(field, out var milliseconds))
            {
                next = WriteValue(next, field, milliseconds);
            }
            else
            {
                valid = false;
            }
        }

        if (!valid)
        {
            return false;
        }

        ApplyPreferences(preferences with { Options = next });
        foreach (var field in Enum.GetValues<TimingPostProcessorField>())
        {
            Restore(field);
        }

        return true;
    }

    /// <summary>恢复单个字段到最新已确认值，其他无效输入保持可编辑。</summary>
    public void Restore(TimingPostProcessorField field)
    {
        SetDraft(field, Format(ReadValue(Options, field)));
        invalidFields.Remove(field);
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>同步已保存偏好，保留尚未确认或无效的本地草稿。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.TimingPostProcessor.Validate();
        foreach (var field in Enum.GetValues<TimingPostProcessorField>())
        {
            if (drafts[field] == Format(ReadValue(Options, field)))
            {
                SetDraft(field, Format(ReadValue(value.TimingPostProcessor.Options, field)));
            }
        }

        preferences = value.TimingPostProcessor;
        RefreshProperties();
    }

    /// <summary>从全局样式库刷新可关联对象，按稳定标识保留目标选择。</summary>
    public void UpdateStyles(IEnumerable<SubtitleStylePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var existing = Styles.ToDictionary(style => style.Id, style => style.IsSelected);
        var primaryId = selectedStyle?.Id;
        var followSavedOptions = selectedStyle?.Options is { } savedOptions && Options == savedOptions &&
            drafts.All(pair => pair.Value == Format(ReadValue(Options, pair.Key)));
        foreach (var style in Styles)
        {
            style.PropertyChanged -= OnStyleChanged;
        }

        refreshingStyles = true;
        try
        {
            Styles.Clear();
            foreach (var preset in presets.OrderBy(preset => preset.Name, StringComparer.CurrentCulture))
            {
                var choice = new TimingStyleChoice(preset.Id, preset.Name, preset.TimingPostProcessor,
                    existing.TryGetValue(preset.Id, out var isSelected) ? isSelected : preset.TimingPostProcessor is not null);
                choice.PropertyChanged += OnStyleChanged;
                Styles.Add(choice);
            }

            selectedStyle = Styles.FirstOrDefault(style => style.Id == primaryId);
            OnPropertyChanged(nameof(SelectedStyle));
        }
        finally
        {
            refreshingStyles = false;
        }

        if (followSavedOptions && selectedStyle?.Options is { } updatedOptions && updatedOptions != Options)
        {
            ApplyPreferences(preferences with { Options = updatedOptions });
            foreach (var timingField in Enum.GetValues<TimingPostProcessorField>())
            {
                Restore(timingField);
            }
        }

        RefreshCommands();
    }

    /// <summary>显示已保存关联的样式数量。</summary>
    public void ShowResult(int count)
    {
        changedCount = count;
        statusKey = "Settings.TimingAssociated";
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>显示成功解除关联的样式数量。</summary>
    public void ShowUnlinked(int count)
    {
        changedCount = count;
        statusKey = "Settings.TimingUnlinked";
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>刷新语言，保留数值草稿及样式身份。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(Status));
    }

    private void RequestAssociation()
    {
        if (CanAssociate && CommitAll())
        {
            AssociateRequested?.Invoke(this, new(Options, SelectedStyleIds()));
        }
    }

    private void RequestUnlink()
    {
        if (CanAssociate)
        {
            UnlinkRequested?.Invoke(this, new(null, SelectedStyleIds()));
        }
    }

    private ImmutableHashSet<Guid> SelectedStyleIds() => Styles.Where(style => style.IsSelected)
        .Select(style => style.Id).ToImmutableHashSet();

    private void SelectStyles(bool value)
    {
        foreach (var style in Styles)
        {
            style.IsSelected = value;
        }
    }

    private void OnStyleChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TimingStyleChoice.IsSelected))
        {
            RefreshCommands();
        }
    }

    private void SetDraft(TimingPostProcessorField field, string value)
    {
        if (drafts[field] != value)
        {
            drafts[field] = value;
            OnPropertyChanged(FieldProperty(field));
        }
    }

    private bool TryRead(TimingPostProcessorField field, out int milliseconds)
    {
        var valid = int.TryParse(drafts[field], NumberStyles.Integer, CultureInfo.CurrentCulture, out milliseconds) && milliseconds >= 0;
        if (valid)
        {
            invalidFields.Remove(field);
        }
        else
        {
            invalidFields.Add(field);
        }

        OnPropertyChanged(nameof(Error));
        return valid;
    }

    private void ApplyPreferences(TimingPostProcessorPreferences value)
    {
        value.Validate();
        if (preferences == value)
        {
            return;
        }

        preferences = value;
        RefreshProperties();
        Changed?.Invoke(this, new(value));
    }

    private void RefreshProperties()
    {
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(BiasPercent));
        OnPropertyChanged(nameof(LeadInEnabled));
        OnPropertyChanged(nameof(LeadOutEnabled));
        OnPropertyChanged(nameof(AdjacencyEnabled));
        OnPropertyChanged(nameof(KeyframeSnapEnabled));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanAssociate));
        AssociateCommand.NotifyCanExecuteChanged();
        UnlinkCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectNoneCommand.NotifyCanExecuteChanged();
    }

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static int ReadValue(TimingPostProcessorOptions options, TimingPostProcessorField field) => field switch
    {
        TimingPostProcessorField.LEAD_IN => options.LeadInMilliseconds,
        TimingPostProcessorField.LEAD_OUT => options.LeadOutMilliseconds,
        TimingPostProcessorField.MAXIMUM_GAP => options.MaximumGapMilliseconds,
        TimingPostProcessorField.MAXIMUM_OVERLAP => options.MaximumOverlapMilliseconds,
        TimingPostProcessorField.START_BEFORE => options.StartBeforeMilliseconds,
        TimingPostProcessorField.START_AFTER => options.StartAfterMilliseconds,
        TimingPostProcessorField.END_BEFORE => options.EndBeforeMilliseconds,
        TimingPostProcessorField.END_AFTER => options.EndAfterMilliseconds,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static TimingPostProcessorOptions WriteValue(TimingPostProcessorOptions options, TimingPostProcessorField field, int value) => field switch
    {
        TimingPostProcessorField.LEAD_IN => options with { LeadInMilliseconds = value },
        TimingPostProcessorField.LEAD_OUT => options with { LeadOutMilliseconds = value },
        TimingPostProcessorField.MAXIMUM_GAP => options with { MaximumGapMilliseconds = value },
        TimingPostProcessorField.MAXIMUM_OVERLAP => options with { MaximumOverlapMilliseconds = value },
        TimingPostProcessorField.START_BEFORE => options with { StartBeforeMilliseconds = value },
        TimingPostProcessorField.START_AFTER => options with { StartAfterMilliseconds = value },
        TimingPostProcessorField.END_BEFORE => options with { EndBeforeMilliseconds = value },
        TimingPostProcessorField.END_AFTER => options with { EndAfterMilliseconds = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static string FieldProperty(TimingPostProcessorField field) => field switch
    {
        TimingPostProcessorField.LEAD_IN => nameof(LeadInMillisecondsText),
        TimingPostProcessorField.LEAD_OUT => nameof(LeadOutMillisecondsText),
        TimingPostProcessorField.MAXIMUM_GAP => nameof(MaximumGapMillisecondsText),
        TimingPostProcessorField.MAXIMUM_OVERLAP => nameof(MaximumOverlapMillisecondsText),
        TimingPostProcessorField.START_BEFORE => nameof(StartBeforeMillisecondsText),
        TimingPostProcessorField.START_AFTER => nameof(StartAfterMillisecondsText),
        TimingPostProcessorField.END_BEFORE => nameof(EndBeforeMillisecondsText),
        TimingPostProcessorField.END_AFTER => nameof(EndAfterMillisecondsText),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
