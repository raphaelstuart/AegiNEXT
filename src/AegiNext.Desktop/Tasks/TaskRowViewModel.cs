using AegiNext.Application.Tasks;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Tasks;

/// <summary>仅保留轻量任务状态的稳定列表行，不持有执行任务或结果。</summary>
public sealed class TaskRowViewModel : ObservableObject
{
    private AegiTaskSnapshot snapshot;
    private Action<Guid>? requestCancellation;
    private double? lastKnownFraction;

    /// <summary>使用任务快照与按标识取消入口创建列表行。</summary>
    public TaskRowViewModel(AegiTaskSnapshot snapshot, Action<Guid> requestCancel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(requestCancel);
        this.snapshot = snapshot;
        lastKnownFraction = snapshot.Progress.Fraction;
        requestCancellation = snapshot.IsFinished ? null : requestCancel;
        CancelCommand = new RelayCommand(RequestCancellation, () => CanCancel);
    }

    public Guid Id => snapshot.Id;
    public string Name => Localization.Get(snapshot.Name);
    public string Stage => snapshot.Progress.Stage is { } stage && !string.IsNullOrWhiteSpace(stage) ?
        Localization.Get(stage) : string.Empty;
    public string Status => Localization.Get("Tasks." + snapshot.State);
    public string SecondaryTitle => snapshot.State == AegiTaskState.Running && !string.IsNullOrWhiteSpace(Stage) &&
        !string.Equals(Name, Stage, StringComparison.Ordinal) ? Stage : Status;
    public string Description => Name + " · " + SecondaryTitle + (HasError ? "\n" + Error : string.Empty);
    public string? Error => snapshot.ErrorSummary;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool CanCancel => snapshot.CanCancel;
    public bool ShowsCancel => snapshot.CanCancel || snapshot.State == AegiTaskState.Cancelling;
    public string CancelName => Localization.Format("Tasks.CancelNamed", Name);
    public bool IsIndeterminate => !snapshot.IsFinished && snapshot.Progress.Fraction is null;
    public double ProgressValue => snapshot.State == AegiTaskState.Succeeded ? 100 :
        (snapshot.Progress.Fraction ?? (snapshot.IsFinished ? lastKnownFraction : null) ?? 0) * 100;
    public string ProgressText => !IsIndeterminate ? Localization.Format("Tasks.Progress", ProgressValue / 100) : string.Empty;
    public string ProgressDescription => ProgressText + (HasError ? "\n" + Error : string.Empty);
    public IRelayCommand CancelCommand { get; }

    /// <summary>更新同一任务行，保留控件绑定、焦点和列表身份。</summary>
    public void Update(AegiTaskSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Id != Id)
        {
            throw new ArgumentException("A task row cannot change its identity.", nameof(value));
        }

        if (snapshot == value)
        {
            return;
        }

        snapshot = value;
        if (snapshot.Progress.Fraction is { } fraction)
        {
            lastKnownFraction = fraction;
        }
        if (snapshot.IsFinished)
        {
            requestCancellation = null;
        }

        RefreshLanguage();
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>刷新任务语义文本及进度格式，不改变任务状态。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SecondaryTitle));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(ShowsCancel));
        OnPropertyChanged(nameof(CancelName));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressDescription));
    }

    private void RequestCancellation()
    {
        if (CanCancel)
        {
            requestCancellation?.Invoke(Id);
        }
    }
}
