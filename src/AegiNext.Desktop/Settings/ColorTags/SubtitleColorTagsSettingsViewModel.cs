using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using AegiNext.Application.ColorTags;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.ColorTags;

/// <summary>个人颜色标签整库草稿；只有保存成功后才接受新的持久化快照。</summary>
public sealed class SubtitleColorTagsSettingsViewModel : ObservableObject, IDisposable
{
    private SubtitleColorTagLibraryDocument original = new();
    private SubtitleColorTagLibraryDocument latest = new();
    private SubtitleColorTagSettingsItem? selectedTag;
    private bool busy;
    private bool saving;
    private bool loading;
    private bool disposed;
    private int revision;
    private string? error;
    private string? errorKey;
    private Task<bool>? pendingSave;

    /// <summary>构造仅包含局部草稿与语义保存回调的标签编辑器。</summary>
    public SubtitleColorTagsSettingsViewModel()
    {
        AddCommand = new(Add, () => IsAvailable);
        DeleteCommand = new(Delete, () => CanEdit);
        SaveCommand = new(SavePendingAsync, () => IsAvailable && IsDirty);
        RestoreCommand = new(DiscardDraft, () => IsAvailable && IsDirty);
    }

    internal Func<SubtitleColorTagLibraryDocument, SubtitleColorTagLibraryDocument,
        Task<SubtitleColorTagLibraryDocument?>>? SaveDraftAsync { get; set; }

    internal Func<Task<int>>? ConfirmLeaveAsync { get; set; }
    internal Task Completion => pendingSave ?? Task.CompletedTask;
    public ObservableCollection<SubtitleColorTagSettingsItem> Tags { get; } = [];
    public RelayCommand AddCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public bool IsEmpty => Tags.Count == 0;
    public bool IsAvailable => !disposed && !busy && !saving;
    public bool CanEdit => IsAvailable && SelectedTag is not null;
    public string? Error => error;
    public bool HasError => Error is not null;

    public bool IsDirty => Tags.Count != original.Tags.Length || Tags.Where((item, index) =>
        item.Id != original.Tags[index].Id || item.Name != original.Tags[index].Name || item.ColorDraft.IsDirty ||
        !string.Equals(item.ColorHex, original.Tags[index].ColorHex, StringComparison.OrdinalIgnoreCase)).Any();

    public SubtitleColorTagSettingsItem? SelectedTag
    {
        get => selectedTag;
        set
        {
            if ((value is null || Tags.Contains(value)) && SetProperty(ref selectedTag, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => busy;
        set
        {
            if (SetProperty(ref busy, value))
            {
                RefreshState();
            }
        }
    }

    /// <summary>同步已提交个人库，保存现有未确认输入。</summary>
    public void UpdateLibrary(SubtitleColorTagLibraryDocument value)
    {
        ArgumentNullException.ThrowIfNull(value);
        latest = value;
        if (!IsDirty && !saving)
        {
            Load(value, SelectedTag?.Id);
        }
    }

    /// <summary>在保存、放弃或取消之后决定是否离开设置页面。</summary>
    public async Task<bool> PrepareToLeaveAsync()
    {
        await Completion;
        if (!IsDirty)
        {
            return true;
        }

        if (ConfirmLeaveAsync is null)
        {
            return false;
        }

        var choice = await ConfirmLeaveAsync();
        if (choice == 1)
        {
            DiscardDraft();
            return true;
        }

        return choice == 0 && await SavePendingAsync();
    }

    /// <summary>验证全部记录后保存一次，失败时保留草稿。</summary>
    public Task<bool> SavePendingAsync()
    {
        if (pendingSave is { IsCompleted: false })
        {
            return pendingSave;
        }

        pendingSave = SaveCoreAsync();
        return pendingSave;
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (!IsAvailable)
        {
            return false;
        }

        if (!IsDirty)
        {
            return true;
        }

        var tags = ImmutableArray.CreateBuilder<SubtitleColorTag>(Tags.Count);
        foreach (var item in Tags)
        {
            var name = item.Name.Trim();
            if (name.Length is < 1 or > SubtitleColorTagValidator.MAXIMUM_NAME_LENGTH || name.Any(char.IsControl))
            {
                SelectedTag = item;
                SetLocalizedError("Settings.ColorTagNameInvalid");
                return false;
            }

            if (!item.ColorDraft.TryCommit(out var color))
            {
                SelectedTag = item;
                SetLocalizedError("Settings.ColorTagColorInvalid");
                return false;
            }

            tags.Add(new() { Id = item.Id, Name = name, ColorHex = ColorHexCodec.Format(color, false) });
        }

        var document = new SubtitleColorTagLibraryDocument { Tags = tags.MoveToImmutable() };
        try
        {
            SubtitleColorTagValidator.Validate(document.Tags);
        }
        catch (InvalidDataException failure)
        {
            SetError(failure.Message);
            return false;
        }

        var save = SaveDraftAsync;
        if (save is null)
        {
            SetLocalizedError("Settings.ColorTagSaveFailed");
            return false;
        }

        var savedRevision = revision;
        var selectedId = SelectedTag?.Id;
        saving = true;
        SetError(null);
        RefreshState();
        try
        {
            var committed = await save(document, original);
            if (committed is null)
            {
                SetLocalizedError("Settings.ColorTagSaveFailed");
                return false;
            }

            if (!disposed)
            {
                latest = committed;
                if (revision == savedRevision)
                {
                    Load(committed, selectedId);
                }
                else
                {
                    original = committed;
                }
            }

            return true;
        }
        finally
        {
            saving = false;
            RefreshState();
        }
    }

    /// <summary>恢复最新已保存个人库，清除尚未保存的标签修改。</summary>
    public void DiscardDraft()
    {
        if (!disposed && !saving)
        {
            Load(latest, SelectedTag?.Id);
        }
    }

    /// <summary>更新错误提示语言，保留全部草稿。</summary>
    public void RefreshLanguage()
    {
        foreach (var item in Tags)
        {
            item.ColorDraft.RefreshLanguage();
        }

        if (errorKey is { } key)
        {
            SetLocalizedError(key);
        }
    }

    private void Load(SubtitleColorTagLibraryDocument value, Guid? selectedId)
    {
        loading = true;
        try
        {
            original = value;
            foreach (var item in Tags)
            {
                item.PropertyChanged -= OnItemChanged;
                item.Dispose();
            }

            Tags.Clear();
            foreach (var tag in value.Tags)
            {
                var item = new SubtitleColorTagSettingsItem(tag);
                item.PropertyChanged += OnItemChanged;
                Tags.Add(item);
            }

            SelectedTag = Tags.FirstOrDefault(item => item.Id == selectedId) ?? Tags.FirstOrDefault();
            SetError(null);
        }
        finally
        {
            loading = false;
        }

        RefreshState();
    }

    private void Add()
    {
        var prefix = Localization.Get("Settings.NewColorTag");
        var names = Tags.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = prefix;
        for (var number = 2; names.Contains(name); number++)
        {
            name = $"{prefix} {number}";
        }

        var item = new SubtitleColorTagSettingsItem(new() { Name = name, ColorHex = "#5273E8" });
        item.PropertyChanged += OnItemChanged;
        Tags.Add(item);
        SelectedTag = item;
        revision++;
        SetError(null);
        RefreshState();
    }

    private void Delete()
    {
        if (SelectedTag is not { } item)
        {
            return;
        }

        var index = Tags.IndexOf(item);
        item.PropertyChanged -= OnItemChanged;
        item.Dispose();
        Tags.Remove(item);
        SelectedTag = Tags.Count == 0 ? null : Tags[Math.Min(index, Tags.Count - 1)];
        revision++;
        SetError(null);
        RefreshState();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!loading && e.PropertyName is nameof(SubtitleColorTagSettingsItem.Name)
                or nameof(SubtitleColorTagSettingsItem.ColorHex))
        {
            revision++;
            SetError(null);
            RefreshState();
        }
    }

    private void SetLocalizedError(string key)
    {
        SetError(Localization.Get(key), key);
    }

    private void SetError(string? value, string? localizationKey = null)
    {
        errorKey = localizationKey;
        if (SetProperty(ref error, value, nameof(Error)))
        {
            OnPropertyChanged(nameof(HasError));
        }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsEmpty));
        AddCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>解除局部草稿订阅，阻止关闭后继续写入。</summary>
    public void Dispose()
    {
        disposed = true;
        SaveDraftAsync = null;
        ConfirmLeaveAsync = null;
        foreach (var item in Tags)
        {
            item.PropertyChanged -= OnItemChanged;
            item.Dispose();
        }

        RefreshState();
    }
}
