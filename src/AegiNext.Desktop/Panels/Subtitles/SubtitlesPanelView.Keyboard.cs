using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed partial class SubtitlesPanelView
{
    private TopLevel? keyboardRoot;
    private bool advancingRow;
    private int rowNavigationRevision;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!disposed)
        {
            keyboardRoot = TopLevel.GetTopLevel(this);
            if (keyboardRoot is Window window)
            {
                window.Deactivated += OnKeyboardRootDeactivated;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseKeyboardRoot();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>仅在字幕内容输入中执行可配置的换行和下一行命令，保留输入法候选确认。</summary>
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return !disposed && !session.IsClosing && !session.IsProjectBusy && !advancingRow && keyboardRoot is not null &&
               (command is WorkbenchCommand.ADVANCE_SUBTITLE_ROW or WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK) &&
               focusedElement is TextBox { AcceptsReturn: true, IsReadOnly: false, DataContext: SubtitleRow row } box &&
               box.GetVisualAncestors().Contains(list) && viewModel.VisibleRows.Any(value => value.Id == row.Id) &&
               list.ContextMenu?.IsOpen != true &&
               !box.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
    }

    /// <summary>在当前内容光标插入换行，或提交后跳转到后续内容输入。</summary>
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }
        var box = (TextBox)focusedElement;
        if (command == WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK)
        {
            box.RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = "\n" });
        }
        else
        {
            _ = AdvanceRowAndFocusAsync((SubtitleRow)box.DataContext!);
        }
        return true;
    }

    private void OnSubtitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !disposed && !e.Handled &&
            e.Source is TextBox { AcceptsReturn: true, DataContext: SubtitleRow } box &&
            !box.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)))
        {
            e.Handled = true;
        }
    }

    private async Task AdvanceRowAndFocusAsync(SubtitleRow row)
    {
        if (advancingRow || keyboardRoot is not { } root)
        {
            return;
        }

        var sourceId = row.Id;
        var trackId = row.Original.TrackId;
        var revision = ++rowNavigationRevision;
        advancingRow = true;
        try
        {
            var targetId = await viewModel.AdvanceRowAsync(sourceId);
            if (targetId is not { } id || disposed || revision != rowNavigationRevision ||
                !ReferenceEquals(root, keyboardRoot) || !this.IsAttachedToVisualTree() ||
                session.CurrentTrackId != trackId || viewModel.SelectedRow?.Id != id)
            {
                return;
            }

            var focused = root.FocusManager?.GetFocusedElement();
            if (focused is not null && (focused is not TextBox { AcceptsReturn: true, DataContext: SubtitleRow focusedRow } ||
                focusedRow.Id != sourceId && focusedRow.Id != id))
            {
                return;
            }

            var target = viewModel.VisibleRows.FirstOrDefault(value => value.Id == id);
            if (target is not null)
            {
                list.ScrollIntoView(target);
                root.UpdateLayout();
                list.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(input =>
                    input.DataContext is SubtitleRow value && value.Id == id && input.AcceptsReturn)?.Focus();
            }
        }
        finally
        {
            advancingRow = false;
        }
    }

    private void OnKeyboardRootDeactivated(object? sender, EventArgs e)
    {
        rowNavigationRevision++;
    }

    private void ReleaseKeyboardRoot()
    {
        rowNavigationRevision++;
        if (keyboardRoot is { } root)
        {
            if (root is Window window)
            {
                window.Deactivated -= OnKeyboardRootDeactivated;
            }
            keyboardRoot = null;
        }
    }
}
