using System.Collections.ObjectModel;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.I18n;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Tasks;

/// <summary>将共享任务状态投影到独立窗口，合并进度通知并保持稳定行身份。</summary>
public sealed class TaskCenterViewModel : ObservableObject, IDisposable
{
    private readonly AegiTaskService service;
    private readonly object notificationGate = new();
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<Guid, TaskRowViewModel> rows = [];
    private Dictionary<Guid, AegiTaskState> observedStates = [];
    private bool hasPendingUpdate;
    private bool disposed;

    /// <summary>在 UI 线程为一个工程主窗口建立任务展示模型。</summary>
    public TaskCenterViewModel(AegiTaskService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Dispatcher.UIThread.VerifyAccess();
        this.service = service;
        ClearHistoryCommand = new RelayCommand(service.ClearHistory, () => HasHistory);
        service.Changed += OnServiceChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        progressTimer.Tick += OnProgressTick;
        progressTimer.Start();
        Apply(service.GetSnapshots());
    }

    public ObservableCollection<TaskRowViewModel> CurrentTasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> RecentTasks { get; } = [];
    public int ActiveCount => CurrentTasks.Count;
    public string ActiveCountText => ActiveCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string ButtonName => Localization.Format("Tasks.Button", ActiveCount);
    public bool HasCurrentTasks => CurrentTasks.Count != 0;
    public bool HasHistory => RecentTasks.Count != 0;
    public bool IsEmpty => !HasCurrentTasks && !HasHistory;
    public IRelayCommand ClearHistoryCommand { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (notificationGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            hasPendingUpdate = false;
            observedStates.Clear();
        }

        service.Changed -= OnServiceChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
        progressTimer.Stop();
        progressTimer.Tick -= OnProgressTick;
        rows.Clear();
        CurrentTasks.Clear();
        RecentTasks.Clear();
    }

    private void OnServiceChanged(object? sender, EventArgs args)
    {
        var snapshots = service.GetSnapshots();
        bool immediate;
        lock (notificationGate)
        {
            if (disposed)
            {
                return;
            }

            immediate = snapshots.Count != observedStates.Count || snapshots.Any(snapshot =>
                !observedStates.TryGetValue(snapshot.Id, out var state) || state != snapshot.State);
            observedStates = snapshots.ToDictionary(snapshot => snapshot.Id, snapshot => snapshot.State);
            hasPendingUpdate = true;
        }

        if (immediate)
        {
            Dispatcher.UIThread.Post(FlushPending);
        }
    }

    private void OnProgressTick(object? sender, EventArgs args) => FlushPending();

    private void FlushPending()
    {
        lock (notificationGate)
        {
            if (disposed || !hasPendingUpdate)
            {
                return;
            }

            hasPendingUpdate = false;
        }

        Apply(service.GetSnapshots());
    }

    private void Apply(IReadOnlyList<AegiTaskSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (!rows.TryGetValue(snapshot.Id, out var row))
            {
                row = new(snapshot, id => service.RequestCancel(id));
                rows.Add(snapshot.Id, row);
            }
            else
            {
                row.Update(snapshot);
            }
        }

        Synchronize(CurrentTasks, snapshots.Where(snapshot => !snapshot.IsFinished)
            .OrderBy(snapshot => snapshot.SubmissionSequence).Select(snapshot => rows[snapshot.Id]).ToArray());
        Synchronize(RecentTasks, snapshots.Where(snapshot => snapshot.IsFinished)
            .OrderByDescending(snapshot => snapshot.FinishedAt).ThenByDescending(snapshot => snapshot.SubmissionSequence)
            .Select(snapshot => rows[snapshot.Id]).ToArray());
        var retained = snapshots.Select(snapshot => snapshot.Id).ToHashSet();
        foreach (var id in rows.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            rows.Remove(id);
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(ButtonName));
        OnPropertyChanged(nameof(HasCurrentTasks));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(IsEmpty));
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    private static void Synchronize(ObservableCollection<TaskRowViewModel> target, TaskRowViewModel[] desired)
    {
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index]))
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < desired.Length; index++)
        {
            var currentIndex = target.IndexOf(desired[index]);
            if (currentIndex < 0)
            {
                target.Insert(index, desired[index]);
            }
            else if (currentIndex != index)
            {
                target.Move(currentIndex, index);
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs args)
    {
        foreach (var row in rows.Values)
        {
            row.RefreshLanguage();
        }

        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(ButtonName));
    }
}
