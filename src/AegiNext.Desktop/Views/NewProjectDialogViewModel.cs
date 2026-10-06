using AegiNext.Application;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Views;

internal sealed class NewProjectDialogViewModel : ObservableObject, IDisposable
{
    private readonly Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create;
    private readonly Func<CancellationToken, Task<string?>> selectLocation;
    private readonly CancellationTokenSource cancellation;
    private string projectName = Localization.Get("Workbench.Untitled");
    private string projectLocation;
    private string? previewPath;
    private string? errorKey;
    private Exception? error;
    private bool isBusy;
    private bool disposed;

    internal NewProjectDialogViewModel(string workspaceRoot,
        Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create,
        Func<CancellationToken, Task<string?>> selectLocation, CancellationToken cancellationToken = default)
    {
        this.create = create;
        this.selectLocation = selectLocation;
        projectLocation = workspaceRoot;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CreateCommand = new(CreateAsync, CanStart);
        BrowseCommand = new(BrowseAsync, CanStart);
        Localization.LanguageChanged += OnLanguageChanged;
        RefreshPreview();
    }

    internal event EventHandler? Created;
    internal bool WasCreated { get; private set; }
    internal Task Completion => Task.WhenAll(CreateCommand.ExecutionTask ?? Task.CompletedTask,
        BrowseCommand.ExecutionTask ?? Task.CompletedTask);

    /// <summary>创建已填写的项目，失败后保持输入。</summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>选择项目的父目录。</summary>
    public AsyncRelayCommand BrowseCommand { get; }

    /// <summary>用户填写的项目名称。</summary>
    public string ProjectName
    {
        get => projectName;
        set
        {
            if (SetProperty(ref projectName, value ?? string.Empty))
            {
                RefreshPreview();
                SetError(null);
            }
        }
    }

    /// <summary>项目所在父目录的输入草稿。</summary>
    public string ProjectLocation
    {
        get => projectLocation;
        set
        {
            if (SetProperty(ref projectLocation, value ?? string.Empty))
            {
                RefreshPreview();
                SetError(null);
            }
        }
    }

    /// <summary>按当前输入计算的最终项目路径。</summary>
    public string PathPreview => previewPath is null ? string.Empty
        : Localization.Format("Workbench.ProjectPathPreview", previewPath);

    /// <summary>创建或目录选择正在进行。</summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                CreateCommand.NotifyCanExecuteChanged();
                BrowseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>允许编辑当前创建草稿。</summary>
    public bool CanInteract => !IsBusy && !disposed;

    /// <summary>当前校验或创建错误。</summary>
    public string? Error => errorKey is null ? error?.Message : Localization.Get(errorKey);

    /// <summary>是否需要显示字段或存储错误。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    private bool CanStart() => !disposed && !IsBusy && !cancellation.IsCancellationRequested;

    private ProjectCreationRequest GetRequest() => new(ProjectName.Trim(), ProjectLocation.Trim());

    private void RefreshPreview()
    {
        try
        {
            previewPath = ProjectCreationService.GetProjectPath(GetRequest());
        }
        catch (Exception failure) when (failure is ArgumentException or IOException)
        {
            previewPath = null;
        }
        OnPropertyChanged(nameof(PathPreview));
    }

    private async Task CreateAsync()
    {
        if (!CanStart())
        {
            return;
        }
        var token = cancellation.Token;
        var request = GetRequest();
        try
        {
            ProjectCreationService.Validate(request);
        }
        catch (ArgumentException failure)
        {
            SetError(failure.ParamName == nameof(ProjectCreationRequest.Name)
                ? string.IsNullOrWhiteSpace(ProjectName) ? "Workbench.NewProjectNameRequired" : "Workbench.NewProjectNameInvalid"
                : string.IsNullOrWhiteSpace(ProjectLocation) ? "Workbench.NewProjectLocationRequired" : "Workbench.NewProjectLocationInvalid");
            return;
        }
        catch (IOException)
        {
            SetError("Workbench.NewProjectDirectoryExists");
            return;
        }
        IsBusy = true;
        SetError(null);
        try
        {
            var result = await create(request, token);
            WasCreated = result.Status == ProjectOpenStatus.OPENED;
            if (disposed || token.IsCancellationRequested)
            {
                return;
            }
            if (result.Status == ProjectOpenStatus.OPENED)
            {
                Created?.Invoke(this, EventArgs.Empty);
            }
            else if (result.Status == ProjectOpenStatus.FAILED)
            {
                SetError(result.Error is null ? "Workbench.NewProjectFailed" : null, result.Error);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            if (!disposed)
            {
                SetError(null, failure);
            }
        }
        finally
        {
            if (!disposed)
            {
                IsBusy = false;
            }
        }
    }

    private async Task BrowseAsync()
    {
        if (!CanStart())
        {
            return;
        }
        var token = cancellation.Token;
        IsBusy = true;
        try
        {
            var selected = await selectLocation(token);
            if (!disposed && !token.IsCancellationRequested && selected is not null)
            {
                ProjectLocation = selected;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            if (!disposed)
            {
                SetError(null, failure);
            }
        }
        finally
        {
            if (!disposed)
            {
                IsBusy = false;
            }
        }
    }

    private void SetError(string? key, Exception? failure = null)
    {
        errorKey = key;
        error = failure;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PathPreview));
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>取消未完成创建并释放语言订阅。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
