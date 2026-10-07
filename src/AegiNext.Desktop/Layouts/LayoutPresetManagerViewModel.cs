using System.Collections.Immutable;
using System.Collections.ObjectModel;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetManagerViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchLayoutController controller;
    private readonly Func<PresetDeletionRequest, CancellationToken, Task<bool>> confirmDeletion;
    private readonly CancellationTokenSource cancellation = new();
    private LayoutPresetRow? selected;
    private ImmutableArray<string> selectedIds = [];
    private string name = string.Empty;
    private string? error;
    private bool isBusy;
    private bool disposed;

    internal LayoutPresetManagerViewModel(WorkbenchLayoutController controller,
        Func<PresetDeletionRequest, CancellationToken, Task<bool>>? confirmDeletion = null)
    {
        this.controller = controller;
        this.confirmDeletion = confirmDeletion ?? ((_, _) => Task.FromResult(false));
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanEdit && SelectedIds.Length == 1);
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => CanEdit && SelectedIds.Length == 1 && Selected is { IsReadOnly: false });
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanEdit && SelectedIds.Length > 0
            && SelectedIds.All(id => Presets.Any(row => row.Id == id && !row.IsReadOnly)));
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, () => CanEdit);
        controller.Changed += OnControllerChanged;
        Refresh();
    }

    internal event EventHandler? ChoicesRefreshing;
    internal event EventHandler? ChoicesRefreshed;
    public ObservableCollection<LayoutPresetRow> Presets { get; } = [];
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand SaveAsCommand { get; }
    public ImmutableArray<string> SelectedIds => selectedIds;
    public bool CanEdit => !isBusy && !disposed;

    public LayoutPresetRow? Selected
    {
        get => selected;
        set => SetSelection(value?.Id, value is null ? [] : [value.Id]);
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    internal void SetSelection(string? primaryId, IEnumerable<string> ids)
    {
        if (!CanEdit)
        {
            return;
        }
        var requested = ids.ToHashSet(StringComparer.Ordinal);
        var next = Presets.Where(row => requested.Contains(row.Id)).Select(row => row.Id).ToImmutableArray();
        UpdateSelection(primaryId, next);
    }

    /// <summary>关闭管理窗口时取消待确认操作并解除控制器订阅。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
        controller.Changed -= OnControllerChanged;
        OnPropertyChanged(nameof(CanEdit));
        NotifyCommands();
    }

    private void UpdateSelection(string? primaryId, ImmutableArray<string> ids)
    {
        selectedIds = ids;
        var row = Presets.FirstOrDefault(value => value.Id == primaryId && ids.Contains(value.Id))
            ?? Presets.FirstOrDefault(value => ids.Contains(value.Id));
        if (SetProperty(ref selected, row, nameof(Selected)))
        {
            Name = row is { IsReadOnly: false } ? row.DisplayName : string.Empty;
        }
        OnPropertyChanged(nameof(SelectedIds));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        ApplyCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync()
    {
        if (Selected is { } row)
        {
            await ExecuteAsync(() => controller.ApplyPresetAsync(row.Id));
        }
    }

    private async Task RenameAsync()
    {
        if (Selected is { } row)
        {
            await ExecuteAsync(() => controller.RenameAsync(row.Id, Name));
        }
    }

    private async Task DeleteAsync()
    {
        var ids = SelectedIds;
        var names = Presets.Where(row => ids.Contains(row.Id)).Select(row => row.DisplayName).ToImmutableArray();
        var token = cancellation.Token;
        await ExecuteAsync(async () =>
        {
            if (!await confirmDeletion(new(names), token).WaitAsync(token))
            {
                return true;
            }
            token.ThrowIfCancellationRequested();
            return !disposed && await controller.DeleteAsync(ids);
        });
    }

    private async Task SaveAsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var id = await controller.SaveAsAsync(Name);
            if (id is not null)
            {
                UpdateSelection(id, [id]);
            }
            return id is not null;
        });
    }

    private async Task ExecuteAsync(Func<Task<bool>> action)
    {
        if (!CanEdit)
        {
            return;
        }
        isBusy = true;
        OnPropertyChanged(nameof(CanEdit));
        NotifyCommands();
        try
        {
            var succeeded = await action();
            if (!disposed)
            {
                Error = succeeded ? null : controller.LastError;
            }
        }
        catch (OperationCanceledException) when (disposed)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!disposed)
            {
                Error = exception.Message;
            }
        }
        finally
        {
            isBusy = false;
            if (!disposed)
            {
                OnPropertyChanged(nameof(CanEdit));
                NotifyCommands();
            }
        }
    }

    private void OnControllerChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        var draftName = Name;
        var previousId = Selected?.Id;
        var ids = SelectedIds;
        var selectedId = Selected?.Id ?? controller.CurrentPresetId;
        ChoicesRefreshing?.Invoke(this, EventArgs.Empty);
        try
        {
            Presets.Clear();
            foreach (var preset in controller.Presets)
            {
                var displayName = WorkbenchLayoutController.GetPresetName(preset);
                if (preset.IsReadOnly)
                {
                    displayName += " (" + Localization.Get("Layout.BuiltIn") + ")";
                }
                Presets.Add(new(preset.Id, displayName, preset.IsReadOnly));
            }
            var retained = Presets.Where(row => ids.Contains(row.Id)).Select(row => row.Id).ToImmutableArray();
            if (retained.IsEmpty && (Presets.FirstOrDefault(row => row.Id == selectedId) ?? Presets.FirstOrDefault()) is { } fallback)
            {
                selectedId = fallback.Id;
                retained = [fallback.Id];
            }
            UpdateSelection(selectedId, retained);
            if (Selected?.Id == previousId)
            {
                Name = draftName;
            }
        }
        finally
        {
            ChoicesRefreshed?.Invoke(this, EventArgs.Empty);
        }
    }
}
