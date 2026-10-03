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

    internal SubtitlesPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
    }

    public SubtitleRow[] Rows
    {
        get => rows;
        set => SetProperty(ref rows, value);
    }

    public SubtitleRow? SelectedRow
    {
        get => selectedRow;
        set => SetProperty(ref selectedRow, value);
    }

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
