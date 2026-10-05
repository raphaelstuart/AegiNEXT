using System.Collections.Immutable;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed class SubtitlesPanelViewModel : ObservableObject
{
    private readonly WorkbenchSession session;
    private SubtitleRow[] rows = [];
    private SubtitleRow[] visibleRows = [];
    private SubtitleRow? selectedRow;
    private string? validationError;
    private Guid? invalidRowId;
    private ImmutableArray<SubtitleTrack> tracks = [];
    private SubtitleTrack? selectedTrack;

    internal SubtitlesPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
    }

    public SubtitleRow[] Rows
    {
        get => rows;
        set
        {
            if (SetProperty(ref rows, value))
            {
                RefreshVisibleRows();
            }
        }
    }

    public SubtitleRow? SelectedRow
    {
        get => selectedRow;
        set => SetProperty(ref selectedRow, value);
    }

    public IReadOnlyList<Guid> SelectedIds => session.SelectedSubtitleIds;

    internal void NotifySelectionChanged() => OnPropertyChanged(nameof(SelectedIds));

    public SubtitleRow[] VisibleRows => visibleRows;
    public ImmutableArray<SubtitleTrack> Tracks => tracks;
    public SubtitleTrack? SelectedTrack => selectedTrack;

    internal void UpdateTracks(ImmutableArray<SubtitleTrack> values, Guid currentId)
    {
        if (!tracks.SequenceEqual(values))
        {
            tracks = values;
            OnPropertyChanged(nameof(Tracks));
        }

        var track = values.Single(value => value.Id == currentId);
        if (selectedTrack != track)
        {
            selectedTrack = track;
            OnPropertyChanged(nameof(SelectedTrack));
        }

        RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        var values = Rows.Where(row => row.Original.TrackId == SelectedTrack?.Id).OrderBy(row => row.Original.Start).ToArray();
        if (!visibleRows.SequenceEqual(values))
        {
            visibleRows = values;
            OnPropertyChanged(nameof(VisibleRows));
        }
    }

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

    public ICommand DetailsCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.OPEN_SUBTITLE_DETAILS);

    public ICommand AddCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.ADD_SUBTITLE);

    public ICommand DeleteCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.DELETE_SUBTITLE);

    public ICommand SplitCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.SPLIT_SUBTITLE);

    public ICommand MergeCueCommand => session.ViewModel.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.MERGE_SUBTITLE);
    /// <summary>选择字幕，切换前统一验证待提交草稿。</summary>
    public void SelectCue(Guid id) => session.SelectCue(id);
    /// <summary>同步字幕列表的主项与完整选择集合，切换前统一验证草稿。</summary>
    public bool SelectRows(Guid? primaryId, IEnumerable<Guid> ids) => session.SelectSubtitleRows(primaryId, ids);
    /// <summary>激活行内编辑目标，保留包含此行的多选集合。</summary>
    public void FocusRow(Guid id) => session.FocusSubtitleRow(id);
    /// <summary>记录文本拆分的字素光标位置。</summary>
    public void SetCaret(Guid id, int index) => session.SetTextCaret(id, index);
    /// <summary>焦点提交使用统一的原子草稿边界。</summary>
    public void CommitRow(SubtitleRow row) => session.CommitRow(row);
}
