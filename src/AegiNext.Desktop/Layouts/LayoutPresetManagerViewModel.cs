using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetManagerViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchLayoutController controller;
    private LayoutPresetRow? selected;
    private string name = string.Empty;
    private string? error;

    internal LayoutPresetManagerViewModel(WorkbenchLayoutController controller)
    {
        this.controller = controller;
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => Selected is not null);
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => Selected is { IsReadOnly: false });
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => Selected is { IsReadOnly: false });
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync);
        controller.Changed += OnControllerChanged;
        Refresh();
    }

    public ObservableCollection<LayoutPresetRow> Presets { get; } = [];
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand RenameCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand SaveAsCommand { get; }

    public LayoutPresetRow? Selected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                Name = value is { IsReadOnly: false } ? value.DisplayName : string.Empty;
                ApplyCommand.NotifyCanExecuteChanged();
                RenameCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
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

    /// <summary>管理窗口关闭后解除布局订阅，保留控制器及未提交的命名草稿。</summary>
    public void Dispose()
    {
        controller.Changed -= OnControllerChanged;
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
        if (Selected is { } row)
        {
            await ExecuteAsync(() => controller.DeleteAsync(row.Id));
        }
    }

    private async Task SaveAsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var id = await controller.SaveAsAsync(Name);
            if (id is not null)
            {
                Selected = Presets.FirstOrDefault(row => row.Id == id);
            }
            return id is not null;
        });
    }

    private async Task ExecuteAsync(Func<Task<bool>> action)
    {
        try
        {
            Error = await action() ? null : controller.LastError;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Error = exception.Message;
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
        var selectedId = Selected?.Id ?? controller.CurrentPresetId;
        Presets.Clear();
        foreach (var preset in controller.Presets)
        {
            var displayName = controller.GetPresetName(preset);
            if (preset.IsReadOnly)
            {
                displayName += " (" + LayoutText.Get("BuiltIn", controller.Culture) + ")";
            }
            Presets.Add(new(preset.Id, displayName, preset.IsReadOnly));
        }
        Selected = Presets.FirstOrDefault(row => row.Id == selectedId) ?? Presets.FirstOrDefault();
        if (Selected?.Id == previousId)
        {
            Name = draftName;
        }
    }
}
