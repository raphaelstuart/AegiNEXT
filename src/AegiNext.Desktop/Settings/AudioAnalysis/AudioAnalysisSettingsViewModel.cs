using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.AudioAnalysis;

/// <summary>保留分析方案草稿，普通参数即时提交，高级方案显式应用。</summary>
public sealed class AudioAnalysisSettingsViewModel : ObservableObject
{
    private readonly Dictionary<AudioAnalysisSettingsField, string> drafts = [];
    private readonly HashSet<AudioAnalysisSettingsField> invalidFields = [];
    private AudioAnalysisPreferences preferences;
    private AudioAnalysisRecipe draftRecipe;
    private bool recipeInvalid;

    /// <summary>从共享偏好创建独立的字段草稿。</summary>
    public AudioAnalysisSettingsViewModel(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        preferences = value.AudioAnalysis;
        draftRecipe = preferences.Recipe;
        foreach (var field in Enum.GetValues<AudioAnalysisSettingsField>())
        {
            drafts[field] = Format(ReadValue(field));
        }
        ApplyCommand = new(() => ApplyRecipe());
        ResetCommand = new(Reset);
    }

    public event EventHandler<AudioAnalysisPreferencesChangedEventArgs>? Changed;
    public event EventHandler<AudioAnalysisRebuildRequestedEventArgs>? RebuildRequested;
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ResetCommand { get; }
    public AudioAnalysisPreferences Preferences => preferences;
    public int MaximumWorkersLimit { get; } = AudioAnalysisExecutionOptions.HardwareMaximumWorkers;
    public bool CanEditMaximumWorkers => !UseAutomaticWorkers;
    public double MaximumFrequencyLimit => draftRecipe.SpectrumSampleRate / 2.0;
    public IReadOnlyList<int> SegmentSizes { get; } = [49152, 98304, 196608, 393216, 786432];
    public IReadOnlyList<int> SampleRates { get; } = [8000, 16000, 24000, 48000];
    public IReadOnlyList<int> FftSizes { get; } = [512, 1024, 2048, 4096];
    public IReadOnlyList<int> HopDivisors { get; } = [2, 4, 8];
    public IReadOnlyList<int> FrequencyBinChoices { get; } = [64, 128, 256, 512];
    public IReadOnlyList<int> WaveformBaseChoices { get; } = [128, 256, 512, 1024, 2048];
    public IReadOnlyList<AudioSpectrumWindowChoice> WindowChoices { get; } =
    [new(AudioSpectrumWindow.HANN), new(AudioSpectrumWindow.HAMMING), new(AudioSpectrumWindow.BLACKMAN)];
    public string WorkersDescription => Localization.Format("Settings.AudioWorkersHelp", MaximumWorkersLimit,
        preferences.Execution.EffectiveMaximumWorkers);
    public string RecipeDescription => Localization.Format("Settings.AudioRecipeEstimate", draftRecipe.WindowMilliseconds,
        draftRecipe.HopMilliseconds, draftRecipe.EstimatedCacheBytesPerSecond * 3600 / (1024 * 1024));
    public string? Error => invalidFields.Count > 0 || recipeInvalid ? Localization.Get("Settings.AudioAnalysisInvalid") : null;

    public bool AdvancedMode
    {
        get => preferences.AdvancedMode;
        set => ApplyPreferences(preferences with { AdvancedMode = value });
    }

    public bool UseAutomaticWorkers
    {
        get => preferences.Execution.MaximumWorkers == 0;
        set
        {
            if (value == UseAutomaticWorkers)
            {
                return;
            }
            var workers = value ? 0 : preferences.Execution.EffectiveMaximumWorkers;
            ApplyPreferences(preferences with { Execution = preferences.Execution with { MaximumWorkers = workers } });
            Restore(AudioAnalysisSettingsField.MAXIMUM_WORKERS);
        }
    }

    public int SegmentSamples
    {
        get => preferences.Execution.SegmentSamples;
        set
        {
            if (SegmentSizes.Contains(value))
            {
                ApplyPreferences(preferences with { Execution = preferences.Execution with { SegmentSamples = value } });
            }
        }
    }

    public int SpectrumSampleRate
    {
        get => draftRecipe.SpectrumSampleRate;
        set
        {
            if (SampleRates.Contains(value))
            {
                UpdateRecipe(draftRecipe with { SpectrumSampleRate = value });
            }
        }
    }
    public int FftSize
    {
        get => draftRecipe.FftSize;
        set
        {
            if (FftSizes.Contains(value))
            {
                UpdateRecipe(draftRecipe with { FftSize = value });
            }
        }
    }
    public int HopDivisor
    {
        get => draftRecipe.HopDivisor;
        set
        {
            if (HopDivisors.Contains(value))
            {
                UpdateRecipe(draftRecipe with { HopDivisor = value });
            }
        }
    }
    public int FrequencyBins
    {
        get => draftRecipe.FrequencyBins;
        set
        {
            if (FrequencyBinChoices.Contains(value))
            {
                UpdateRecipe(draftRecipe with { FrequencyBins = value });
            }
        }
    }
    public int WaveformBaseSamples
    {
        get => draftRecipe.WaveformBaseSamples;
        set
        {
            if (WaveformBaseChoices.Contains(value))
            {
                UpdateRecipe(draftRecipe with { WaveformBaseSamples = value });
            }
        }
    }
    public AudioSpectrumWindowChoice? SelectedWindow
    {
        get => WindowChoices.FirstOrDefault(choice => choice.Window == draftRecipe.Window);
        set
        {
            if (value is not null)
            {
                UpdateRecipe(draftRecipe with { Window = value.Window });
            }
        }
    }

    public string MaximumWorkersText
    {
        get => drafts[AudioAnalysisSettingsField.MAXIMUM_WORKERS];
        set => SetDraft(AudioAnalysisSettingsField.MAXIMUM_WORKERS, value);
    }

    public string MemoryBudgetText
    {
        get => drafts[AudioAnalysisSettingsField.MEMORY_BUDGET];
        set => SetDraft(AudioAnalysisSettingsField.MEMORY_BUDGET, value);
    }

    public string WaveformGainText
    {
        get => drafts[AudioAnalysisSettingsField.WAVEFORM_GAIN];
        set => SetDraft(AudioAnalysisSettingsField.WAVEFORM_GAIN, value);
    }

    public string SpectrumBrightnessText
    {
        get => drafts[AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS];
        set => SetDraft(AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS, value);
    }

    public string SpectrumContrastText
    {
        get => drafts[AudioAnalysisSettingsField.SPECTRUM_CONTRAST];
        set => SetDraft(AudioAnalysisSettingsField.SPECTRUM_CONTRAST, value);
    }

    public string MinimumFrequencyText
    {
        get => drafts[AudioAnalysisSettingsField.MINIMUM_FREQUENCY];
        set => SetDraft(AudioAnalysisSettingsField.MINIMUM_FREQUENCY, value);
    }

    public string MaximumFrequencyText
    {
        get => drafts[AudioAnalysisSettingsField.MAXIMUM_FREQUENCY];
        set => SetDraft(AudioAnalysisSettingsField.MAXIMUM_FREQUENCY, value);
    }

    public string MinimumDecibelsText
    {
        get => drafts[AudioAnalysisSettingsField.MINIMUM_DECIBELS];
        set => SetDraft(AudioAnalysisSettingsField.MINIMUM_DECIBELS, value);
    }

    public string MaximumDecibelsText
    {
        get => drafts[AudioAnalysisSettingsField.MAXIMUM_DECIBELS];
        set => SetDraft(AudioAnalysisSettingsField.MAXIMUM_DECIBELS, value);
    }

    /// <summary>确认单个数字，分析参数仍只写入草稿。</summary>
    public bool Commit(AudioAnalysisSettingsField field)
    {
        if (field == AudioAnalysisSettingsField.MAXIMUM_WORKERS && UseAutomaticWorkers)
        {
            Restore(field);
            return true;
        }
        if (!double.TryParse(drafts[field], NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            !double.IsFinite(value) || !IsValid(field, value))
        {
            invalidFields.Add(field);
            OnPropertyChanged(nameof(Error));
            return false;
        }
        switch (field)
        {
            case AudioAnalysisSettingsField.MAXIMUM_WORKERS:
                ApplyPreferences(preferences with { Execution = preferences.Execution with { MaximumWorkers = (int)value } });
                break;
            case AudioAnalysisSettingsField.MEMORY_BUDGET:
                ApplyPreferences(preferences with { Execution = preferences.Execution with { MemoryBudgetMiB = (int)value } });
                break;
            case AudioAnalysisSettingsField.WAVEFORM_GAIN:
                ApplyPreferences(preferences with { Display = preferences.Display with { WaveformGain = value } });
                break;
            case AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS:
                ApplyPreferences(preferences with { Display = preferences.Display with { SpectrumBrightness = value } });
                break;
            case AudioAnalysisSettingsField.SPECTRUM_CONTRAST:
                ApplyPreferences(preferences with { Display = preferences.Display with { SpectrumContrast = value } });
                break;
            case AudioAnalysisSettingsField.MINIMUM_FREQUENCY:
                UpdateRecipe(draftRecipe with { MinimumFrequency = value });
                break;
            case AudioAnalysisSettingsField.MAXIMUM_FREQUENCY:
                UpdateRecipe(draftRecipe with { MaximumFrequency = value });
                break;
            case AudioAnalysisSettingsField.MINIMUM_DECIBELS:
                UpdateRecipe(draftRecipe with { MinimumDecibels = value });
                break;
            case AudioAnalysisSettingsField.MAXIMUM_DECIBELS:
                UpdateRecipe(draftRecipe with { MaximumDecibels = value });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
        Restore(field);
        return true;
    }

    /// <summary>恢复该字段最后确认的值，不重置其他草稿。</summary>
    public void Restore(AudioAnalysisSettingsField field)
    {
        SetDraft(field, Format(ReadValue(field)));
        invalidFields.Remove(field);
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>验证全部高级草稿并请求应用；相同方案也允许显式重新生成。</summary>
    public bool ApplyRecipe()
    {
        var valid = true;
        foreach (var field in Enum.GetValues<AudioAnalysisSettingsField>().Where(IsRecipeField))
        {
            valid &= Commit(field);
        }
        ValidateRecipe();
        if (!valid || recipeInvalid)
        {
            return false;
        }
        preferences = preferences with { Recipe = draftRecipe };
        OnPropertyChanged(nameof(Preferences));
        RebuildRequested?.Invoke(this, new(preferences));
        return true;
    }

    /// <summary>普通参数恢复默认立即生效，高级默认保留为待应用草稿。</summary>
    public void Reset()
    {
        var defaults = new AudioAnalysisPreferences();
        ApplyPreferences(preferences with { Execution = defaults.Execution, Display = defaults.Display });
        draftRecipe = defaults.Recipe;
        foreach (var field in Enum.GetValues<AudioAnalysisSettingsField>())
        {
            Restore(field);
        }
        RefreshRecipe();
    }

    /// <summary>回填共享偏好，保留未确认原文及尚未应用的高级草稿。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        value.Validate();
        var previous = preferences;
        var recipeUntouched = draftRecipe == previous.Recipe && Enum.GetValues<AudioAnalysisSettingsField>()
            .Where(IsRecipeField).All(field => drafts[field] == Format(ReadValue(field)));
        var restoreFields = Enum.GetValues<AudioAnalysisSettingsField>().Where(field =>
            !IsRecipeField(field) && drafts[field] == Format(ReadValue(field))).ToArray();
        preferences = value.AudioAnalysis;
        if (recipeUntouched)
        {
            draftRecipe = preferences.Recipe;
            foreach (var field in Enum.GetValues<AudioAnalysisSettingsField>().Where(IsRecipeField))
            {
                Restore(field);
            }
        }
        foreach (var field in restoreFields)
        {
            Restore(field);
        }
        RefreshPreferences();
        RefreshRecipe();
    }

    /// <summary>刷新动态说明及窗函数名称，不提交草稿。</summary>
    public void RefreshLanguage()
    {
        foreach (var choice in WindowChoices)
        {
            choice.RefreshLanguage();
        }
        OnPropertyChanged(nameof(WorkersDescription));
        OnPropertyChanged(nameof(RecipeDescription));
        OnPropertyChanged(nameof(Error));
    }

    private void ApplyPreferences(AudioAnalysisPreferences value)
    {
        value.Validate();
        if (preferences == value)
        {
            return;
        }
        preferences = value;
        RefreshPreferences();
        Changed?.Invoke(this, new(value));
    }

    private void RefreshPreferences()
    {
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(AdvancedMode));
        OnPropertyChanged(nameof(UseAutomaticWorkers));
        OnPropertyChanged(nameof(CanEditMaximumWorkers));
        OnPropertyChanged(nameof(SegmentSamples));
        OnPropertyChanged(nameof(WorkersDescription));
    }

    private void UpdateRecipe(AudioAnalysisRecipe value)
    {
        if (draftRecipe == value)
        {
            return;
        }
        draftRecipe = value;
        RefreshRecipe();
    }

    private void RefreshRecipe()
    {
        ValidateRecipe();
        OnPropertyChanged(nameof(SpectrumSampleRate));
        OnPropertyChanged(nameof(FftSize));
        OnPropertyChanged(nameof(HopDivisor));
        OnPropertyChanged(nameof(FrequencyBins));
        OnPropertyChanged(nameof(WaveformBaseSamples));
        OnPropertyChanged(nameof(SelectedWindow));
        OnPropertyChanged(nameof(MaximumFrequencyLimit));
        OnPropertyChanged(nameof(RecipeDescription));
    }

    private void ValidateRecipe()
    {
        try
        {
            draftRecipe.Validate();
            recipeInvalid = false;
        }
        catch (InvalidDataException)
        {
            recipeInvalid = true;
        }
        OnPropertyChanged(nameof(Error));
    }

    private void SetDraft(AudioAnalysisSettingsField field, string value)
    {
        if (drafts[field] != value)
        {
            drafts[field] = value;
            OnPropertyChanged(field switch
            {
                AudioAnalysisSettingsField.MAXIMUM_WORKERS => nameof(MaximumWorkersText),
                AudioAnalysisSettingsField.MEMORY_BUDGET => nameof(MemoryBudgetText),
                AudioAnalysisSettingsField.WAVEFORM_GAIN => nameof(WaveformGainText),
                AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS => nameof(SpectrumBrightnessText),
                AudioAnalysisSettingsField.SPECTRUM_CONTRAST => nameof(SpectrumContrastText),
                AudioAnalysisSettingsField.MINIMUM_FREQUENCY => nameof(MinimumFrequencyText),
                AudioAnalysisSettingsField.MAXIMUM_FREQUENCY => nameof(MaximumFrequencyText),
                AudioAnalysisSettingsField.MINIMUM_DECIBELS => nameof(MinimumDecibelsText),
                AudioAnalysisSettingsField.MAXIMUM_DECIBELS => nameof(MaximumDecibelsText),
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            });
        }
    }

    private double ReadValue(AudioAnalysisSettingsField field)
    {
        return field switch
        {
            AudioAnalysisSettingsField.MAXIMUM_WORKERS => Math.Max(1, preferences.Execution.MaximumWorkers),
            AudioAnalysisSettingsField.MEMORY_BUDGET => preferences.Execution.MemoryBudgetMiB,
            AudioAnalysisSettingsField.WAVEFORM_GAIN => preferences.Display.WaveformGain,
            AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS => preferences.Display.SpectrumBrightness,
            AudioAnalysisSettingsField.SPECTRUM_CONTRAST => preferences.Display.SpectrumContrast,
            AudioAnalysisSettingsField.MINIMUM_FREQUENCY => draftRecipe.MinimumFrequency,
            AudioAnalysisSettingsField.MAXIMUM_FREQUENCY => draftRecipe.MaximumFrequency,
            AudioAnalysisSettingsField.MINIMUM_DECIBELS => draftRecipe.MinimumDecibels,
            AudioAnalysisSettingsField.MAXIMUM_DECIBELS => draftRecipe.MaximumDecibels,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private bool IsValid(AudioAnalysisSettingsField field, double value)
    {
        return field switch
        {
            AudioAnalysisSettingsField.MAXIMUM_WORKERS => value.Equals(Math.Truncate(value)) && value >= 1 &&
                (value <= MaximumWorkersLimit || value.Equals(preferences.Execution.MaximumWorkers)),
            AudioAnalysisSettingsField.MEMORY_BUDGET => value.Equals(Math.Truncate(value)) && value is >= 32 and <= 512,
            AudioAnalysisSettingsField.WAVEFORM_GAIN => value is >= 0.25 and <= 8,
            AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS or AudioAnalysisSettingsField.SPECTRUM_CONTRAST => value is >= 0.25 and <= 4,
            AudioAnalysisSettingsField.MINIMUM_FREQUENCY => value >= 10 && value < MaximumFrequencyLimit,
            AudioAnalysisSettingsField.MAXIMUM_FREQUENCY => value >= 10 && value <= MaximumFrequencyLimit,
            AudioAnalysisSettingsField.MINIMUM_DECIBELS => value is >= -160 and <= -10,
            AudioAnalysisSettingsField.MAXIMUM_DECIBELS => value is >= -40 and <= 20,
            _ => false
        };
    }

    private static bool IsRecipeField(AudioAnalysisSettingsField field)
    {
        return field is AudioAnalysisSettingsField.MINIMUM_FREQUENCY or AudioAnalysisSettingsField.MAXIMUM_FREQUENCY or
            AudioAnalysisSettingsField.MINIMUM_DECIBELS or AudioAnalysisSettingsField.MAXIMUM_DECIBELS;
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.CurrentCulture);
    }
}
