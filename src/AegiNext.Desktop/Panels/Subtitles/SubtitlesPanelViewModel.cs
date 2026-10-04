using System.Collections.Immutable;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using Avalonia.Media;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed class SubtitlesPanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private SubtitleRow[] rows = [];
    private SubtitleRow? selectedRow;
    private string? validationError;
    private Guid? invalidRowId;
    private ImmutableArray<SubtitleTrack> tracks = [];
    private SubtitleTrack? selectedTrack;
    private SubtitleTrack? targetTrack;
    private string trackName = string.Empty;
    private bool trackNameDirty;

    internal SubtitlesPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        AddTrackCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.AddSubtitleTrack)));
        RenameTrackCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.RenameSubtitleTrack)));
        DeleteTrackCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(session.RemoveSubtitleTrack)));
        MoveTrackUpCommand = new(() => session.MoveCurrentSubtitleTrackAsync(-1), () => CanMoveTrackUp);
        MoveTrackDownCommand = new(() => session.MoveCurrentSubtitleTrackAsync(1), () => CanMoveTrackDown);
        MoveToTrackCommand = new AsyncRelayCommand(() => session.RunCommandAsync(() => session.EditAsync(() =>
        {
            if (SelectedRow is { } row && TargetTrack is { } track)
            {
                session.MoveSubtitleToTrack(row.Id, track.Id);
            }
        })));
    }

    public SubtitleRow[] Rows
    {
        get => rows;
        set
        {
            if (SetProperty(ref rows, value))
            {
                OnPropertyChanged(nameof(VisibleRows));
                OnPropertyChanged(nameof(CanDeleteTrack));
            }
        }
    }

    public SubtitleRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (SetProperty(ref selectedRow, value))
            {
                OnPropertyChanged(nameof(CanMoveToTrack));
            }
        }
    }

    public SubtitleRow[] VisibleRows => Rows.Where(row => row.Original.TrackId == SelectedTrack?.Id)
        .OrderBy(row => row.Original.Start).ToArray();
    public ImmutableArray<SubtitleTrack> Tracks => tracks;
    public SubtitleTrack? SelectedTrack => selectedTrack;
    public bool CanDeleteTrack => Tracks.Length > 1 && SelectedTrack is { } track && !Rows.Any(row => row.Original.TrackId == track.Id);
    public bool CanMoveToTrack => SelectedRow is not null && TargetTrack is { } track && track.Id != SelectedTrack?.Id;
    public bool CanMoveTrackUp => SelectedTrackIndex > 0;
    public bool CanMoveTrackDown => SelectedTrackIndex >= 0 && SelectedTrackIndex < Tracks.Length - 1;
    public ICommand AddTrackCommand { get; }
    public ICommand RenameTrackCommand { get; }
    public ICommand DeleteTrackCommand { get; }
    public ICommand MoveToTrackCommand { get; }
    public AsyncRelayCommand MoveTrackUpCommand { get; }
    public AsyncRelayCommand MoveTrackDownCommand { get; }
    private int SelectedTrackIndex => SelectedTrack is { } track ? Tracks.IndexOf(track) : -1;

    public SubtitleTrack? TargetTrack
    {
        get => targetTrack;
        set
        {
            if (SetProperty(ref targetTrack, value))
            {
                OnPropertyChanged(nameof(CanMoveToTrack));
            }
        }
    }

    public string TrackName
    {
        get => trackName;
        set
        {
            if (SetProperty(ref trackName, value))
            {
                trackNameDirty = true;
            }
        }
    }

    internal void UpdateTracks(ImmutableArray<SubtitleTrack> values, Guid currentId)
    {
        var sameTrack = selectedTrack?.Id == currentId;
        tracks = values;
        selectedTrack = values.Single(track => track.Id == currentId);
        if (!sameTrack || !trackNameDirty)
        {
            TrackName = selectedTrack.Name;
            trackNameDirty = false;
        }

        OnPropertyChanged(nameof(Tracks));
        OnPropertyChanged(nameof(SelectedTrack));
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(CanDeleteTrack));
        OnPropertyChanged(nameof(CanMoveToTrack));
        OnPropertyChanged(nameof(CanMoveTrackUp));
        OnPropertyChanged(nameof(CanMoveTrackDown));
        MoveTrackUpCommand.NotifyCanExecuteChanged();
        MoveTrackDownCommand.NotifyCanExecuteChanged();
        if (TargetTrack is not null && !values.Contains(TargetTrack))
        {
            TargetTrack = values.FirstOrDefault(track => track.Id == TargetTrack.Id);
        }
    }

    internal void AcceptTrackName() => trackNameDirty = false;

    /// <summary>切换当前字幕轨道，保留全部行草稿的统一提交边界。</summary>
    public void SelectTrack(Guid id) => session.SelectTrack(id);

    public string? ValidationError
    {
        get => validationError;
        set => SetProperty(ref validationError, value);
    }

    public Guid? InvalidRowId
    {
        get => invalidRowId;
        set => SetProperty(ref invalidRowId, value);
    }

    public ICommand AddCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.ADD_SUBTITLE);

    public ICommand DeleteCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.DELETE_SUBTITLE);

    public ICommand SplitCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.SPLIT_SUBTITLE);

    public ICommand MergeCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.MERGE_SUBTITLE);
    /// <summary>选择字幕，切换前统一验证待提交草稿。</summary>
    public void SelectCue(Guid id) => session.SelectCue(id);
    /// <summary>记录文本拆分的字素光标位置。</summary>
    public void SetCaret(Guid id, int index) => session.SetTextCaret(id, index);
    /// <summary>焦点提交使用统一的原子草稿边界。</summary>
    public void CommitRow(SubtitleRow row) => session.CommitRow(row);
}
