using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Startup;

internal sealed class WelcomeViewModel : ObservableObject, IDisposable
{
    private readonly RecentProjectService history;
    private string searchText = string.Empty;
    private RecentProjectListItem[] projects = [];
    private RecentProjectListItem? selectedProject;
    private bool isBusy;
    private string? error;
    private string title = Localization.Get("Welcome.Title");

    internal WelcomeViewModel(RecentProjectService history, Func<Task> createProject,
        Func<string?, Task> openProject, Func<Task> openSettings)
    {
        this.history = history;
        NewProjectCommand = new(createProject, CanStart);
        OpenProjectCommand = new(() => openProject(null), CanStart);
        OpenSelectedProjectCommand = new(() => openProject(SelectedProject!.Path),
            () => CanStart() && SelectedProject is { IsUnavailable: false });
        SettingsCommand = new(openSettings, CanStart);
        RemoveProjectCommand = new(async item =>
        {
            if (item is not null)
            {
                await history.RemoveAsync(item.Path);
            }
        }, item => CanStart() && item is not null);
        history.Changed += OnHistoryChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        RefreshProjects();
    }

    public AsyncRelayCommand NewProjectCommand { get; }
    public AsyncRelayCommand OpenProjectCommand { get; }
    public AsyncRelayCommand OpenSelectedProjectCommand { get; }
    public AsyncRelayCommand SettingsCommand { get; }
    public AsyncRelayCommand<RecentProjectListItem> RemoveProjectCommand { get; }
    public IReadOnlyList<RecentProjectListItem> Projects => projects;
    public bool HasProjects => projects.Length > 0;
    public bool IsEmpty => !HasProjects;
    public string Title => title;
    public string EmptyTitle => Localization.Get(string.IsNullOrWhiteSpace(SearchText)
        ? "Welcome.NoRecentProjects" : "Welcome.NoMatchingProjects");
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool CanInteract => !IsBusy;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                RefreshProjects();
            }
        }
    }

    public RecentProjectListItem? SelectedProject
    {
        get => selectedProject;
        set
        {
            if (SetProperty(ref selectedProject, value))
            {
                OpenSelectedProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        internal set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                NewProjectCommand.NotifyCanExecuteChanged();
                OpenProjectCommand.NotifyCanExecuteChanged();
                OpenSelectedProjectCommand.NotifyCanExecuteChanged();
                SettingsCommand.NotifyCanExecuteChanged();
                RemoveProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Error
    {
        get => error;
        internal set
        {
            if (SetProperty(ref error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    internal void RefreshAvailability()
    {
        foreach (var project in projects)
        {
            project.Refresh();
        }
        OpenSelectedProjectCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart() => !IsBusy;
    private void OnHistoryChanged(object? sender, EventArgs e) => RefreshProjects();

    private void RefreshProjects()
    {
        var selectedPath = SelectedProject?.Path;
        var query = SearchText.Trim();
        projects = history.Entries.Where(entry => query.Length == 0 ||
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new RecentProjectListItem(entry)).ToArray();
        OnPropertyChanged(nameof(Projects));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        SelectedProject = projects.FirstOrDefault(item => item.Path == selectedPath) ?? projects.FirstOrDefault();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        title = Localization.Get("Welcome.Title");
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyTitle));
        RefreshAvailability();
    }

    public void Dispose()
    {
        history.Changed -= OnHistoryChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
    }
}
