using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.Effects;

/// <summary>个人特效脚本编辑与管理模型；只保存纯文本草稿并发出可等待工作流的语义请求。</summary>
public sealed class EffectSettingsViewModel : ObservableObject
{
    private ImmutableArray<EffectScriptPreset> presets = [];
    private ImmutableArray<EffectScriptSettingsItem> items = [];
    private readonly Dictionary<Guid, EffectScriptPreset> drafts = new();
    private EffectScriptSettingsItem? selectedEffect;
    private Guid? loadedDraftId;
    private string name = string.Empty;
    private string source = string.Empty;
    private string? error;
    private string? validationStatus;
    private int diagnosticLine;
    private int diagnosticColumn = 1;
    private bool loading;
    private bool busy;

    /// <summary>创建独立页面命令；实际个人库读写由组合根承担。</summary>
    public EffectSettingsViewModel()
    {
        AddCommand = new(Add, () => !IsBusy);
        DuplicateCommand = new(Duplicate, () => HasSelection && !IsBusy);
        DeleteCommand = new(() => DeleteRequested?.Invoke(this, new(SelectedEffect!.Id)), () => CanDelete);
        SaveCommand = new(() => Submit(true, SaveRequested), () => CanEdit);
        ExportCommand = new(() => Submit(false, ExportRequested), () => HasSelection && !IsBusy);
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        ValidateCommand = new(Validate, () => HasSelection && !IsBusy);
        UpdateEffects([]);
    }

    public event EventHandler<SettingsEffectEventArgs>? SaveRequested;
    public event EventHandler<SettingsEffectDeleteEventArgs>? DeleteRequested;
    public event EventHandler<SettingsEffectEventArgs>? ExportRequested;
    public event EventHandler<EffectScriptValidationFailedEventArgs>? ValidationFailed;
    public event EventHandler? ImportRequested;
    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public ImmutableArray<EffectScriptSettingsItem> Effects => items;
    public bool HasSelection => selectedEffect is not null;
    public bool IsReadOnly => selectedEffect?.IsBuiltin ?? true;
    public bool CanEdit => HasSelection && !IsReadOnly && !IsBusy;
    public bool CanDelete => CanEdit && presets.Any(item => item.Id == selectedEffect!.Id);
    public bool IsDirty => selectedEffect is not null && drafts.ContainsKey(selectedEffect.Id);
    public string EditorStatus => Localization.Get("Settings." + (IsReadOnly ? "BuiltinScriptReadOnly" : IsDirty ? "ScriptUnsaved" : "ScriptSaved"));

    public EffectScriptSettingsItem? SelectedEffect
    {
        get => selectedEffect;
        set
        {
            if (SetProperty(ref selectedEffect, value) && !loading)
            {
                LoadDraft();
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                RecordDraft(false);
            }
        }
    }

    public string Source
    {
        get => source;
        set
        {
            if (SetProperty(ref source, value))
            {
                RecordDraft(true);
            }
        }
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    public string? ValidationStatus
    {
        get => validationStatus;
        private set => SetProperty(ref validationStatus, value);
    }

    public int DiagnosticLine
    {
        get => diagnosticLine;
        private set => SetProperty(ref diagnosticLine, value);
    }

    public int DiagnosticColumn
    {
        get => diagnosticColumn;
        private set => SetProperty(ref diagnosticColumn, value);
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

    /// <summary>同步成功提交的个人库；其他模板的未保存草稿继续保留。</summary>
    public void UpdateEffects(IEnumerable<EffectScriptPreset> values, Guid? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var updated = values.ToImmutableArray();
        var removed = presets.Select(item => item.Id).Except(updated.Select(item => item.Id));
        foreach (var id in removed)
        {
            drafts.Remove(id);
        }

        presets = updated;
        foreach (var preset in presets)
        {
            if (drafts.TryGetValue(preset.Id, out var pending) && pending.Source == preset.Source &&
                string.Equals(pending.Name.Trim(), preset.Name, StringComparison.Ordinal))
            {
                drafts.Remove(preset.Id);
            }
        }

        if (selectedId is { } savedId)
        {
            drafts.Remove(savedId);
        }

        RebuildItems(selectedId ?? selectedEffect?.Id);
    }

    /// <summary>语言切换仅更换内置名称与提示，个人名称和全部源代码保持原样。</summary>
    public void RefreshLanguage()
    {
        RebuildItems(selectedEffect?.Id);
        OnPropertyChanged(nameof(EditorStatus));
        if (validationStatus is not null && Error is null)
        {
            ValidationStatus = Localization.Get("Settings.ScriptValid");
        }
    }

    private void RebuildItems(Guid? selectedId)
    {
        var wasLoading = loading;
        loading = true;
        try
        {
            var builtins = BuiltinEffectScripts.Templates.Select(template =>
            {
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(template.Script.Id)).AsSpan(0, 16));
                return new EffectScriptSettingsItem(new(id, Localization.Get("Settings.Effect_" + template.Script.Id), template.Source), true);
            });
            var custom = presets.Concat(drafts.Values.Where(draft => presets.All(item => item.Id != draft.Id)))
                .Select(preset => new EffectScriptSettingsItem(preset, false));
            items = builtins.Concat(custom).ToImmutableArray();
            OnPropertyChanged(nameof(Effects));
            SelectedEffect = items.FirstOrDefault(item => item.Id == selectedId) ?? items.FirstOrDefault();
        }
        finally
        {
            loading = wasLoading;
        }
        LoadDraft();
    }

    private void LoadDraft()
    {
        loading = true;
        var value = selectedEffect is null ? null : drafts.GetValueOrDefault(selectedEffect.Id) ?? selectedEffect.Preset;
        var preserveDiagnostics = loadedDraftId == selectedEffect?.Id && Source == (value?.Source ?? string.Empty) &&
            Name == (value?.Name ?? string.Empty);
        Name = value?.Name ?? string.Empty;
        Source = value?.Source ?? string.Empty;
        loadedDraftId = selectedEffect?.Id;
        loading = false;
        if (!preserveDiagnostics)
        {
            ClearDiagnostics();
        }
        RefreshActions();
    }

    private void RecordDraft(bool sourceChanged)
    {
        if (loading || selectedEffect is null || IsReadOnly)
        {
            return;
        }

        var value = new EffectScriptPreset(selectedEffect.Id, Name, Source);
        if (presets.FirstOrDefault(item => item.Id == value.Id) == value)
        {
            drafts.Remove(value.Id);
        }
        else
        {
            drafts[value.Id] = value;
        }

        if (sourceChanged || DiagnosticLine == 0)
        {
            ClearDiagnostics();
        }
        RefreshActions();
    }

    private void Add()
    {
        var id = Guid.NewGuid();
        var scriptId = "effect-" + id.ToString("N");
        var template = new EffectScriptPreset(id, UniqueName(Localization.Get("Settings.NewEffect")),
            $"effect \"{scriptId}\" version 1\nshort-clip compress\nsegment enter fixed 300ms\n    at 0 opacity 0 ease-out\n    at 1 opacity base\nend\nsegment stay flex 1\n    at 0 opacity base hold\n    at 1 opacity base\nend\n");
        drafts[id] = template;
        RebuildItems(id);
    }

    private void Duplicate()
    {
        if (selectedEffect is null)
        {
            return;
        }

        var id = Guid.NewGuid();
        var scriptId = "effect-" + id.ToString("N");
        var rewritten = Regex.Replace(Source, "^effect\\s+\"[^\"]+\"", $"effect \"{scriptId}\"", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        drafts[id] = new(id, UniqueName(Name + " " + Localization.Get("Settings.CopySuffix")), rewritten);
        RebuildItems(id);
    }

    private string UniqueName(string proposed)
    {
        var names = items.Select(item => drafts.GetValueOrDefault(item.Id)?.Name ?? item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = proposed;
        var index = 2;
        while (names.Contains(result))
        {
            result = $"{proposed} {index++}";
        }

        return result;
    }

    private void Validate()
    {
        _ = ValidateDraft(false);
    }

    private bool ValidateDraft(bool saving)
    {
        if (selectedEffect is null)
        {
            return false;
        }

        try
        {
            var preset = new EffectScriptPreset(selectedEffect.Id, Name.Trim(), Source);
            if (saving)
            {
                var values = presets.Where(item => item.Id != preset.Id).Append(preset).ToImmutableArray();
                EffectScriptPresetService.Validate(new EffectScriptPresetDocument { Presets = values });
            }
            else
            {
                _ = EffectScriptParser.Parse(Source);
            }

            ClearDiagnostics();
            ValidationStatus = Localization.Get("Settings.ScriptValid");
            return true;
        }
        catch (EffectScriptException failure)
        {
            Error = failure.Message;
            DiagnosticLine = failure.Line;
            DiagnosticColumn = failure.Column;
            ValidationStatus = null;
            ValidationFailed?.Invoke(this, new(failure));
            return false;
        }
        catch (InvalidDataException failure)
        {
            Error = failure.Message;
            ValidationStatus = null;
            ValidationFailed?.Invoke(this, new(failure));
            return false;
        }
    }

    private void Submit(bool saving, EventHandler<SettingsEffectEventArgs>? handler)
    {
        if (selectedEffect is not null && ValidateDraft(saving))
        {
            handler?.Invoke(this, new(new(selectedEffect.Id, Name.Trim(), Source)));
        }
    }

    private void ClearDiagnostics()
    {
        Error = null;
        ValidationStatus = null;
        DiagnosticLine = 0;
        DiagnosticColumn = 1;
    }

    private void RefreshActions()
    {
        foreach (var property in new[] { nameof(HasSelection), nameof(IsReadOnly), nameof(CanEdit), nameof(CanDelete), nameof(IsDirty), nameof(EditorStatus) })
        {
            OnPropertyChanged(property);
        }

        foreach (var command in new[] { AddCommand, DuplicateCommand, DeleteCommand, SaveCommand, ExportCommand, ImportCommand, ValidateCommand })
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
