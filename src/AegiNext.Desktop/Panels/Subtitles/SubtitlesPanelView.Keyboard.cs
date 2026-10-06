using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed partial class SubtitlesPanelView
{
    private TopLevel? keyboardRoot;
    private bool enterPressed;
    private bool advancingRow;
    private int rowNavigationRevision;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!disposed)
        {
            keyboardRoot = TopLevel.GetTopLevel(this);
            keyboardRoot?.AddHandler(KeyUpEvent, OnSubtitleKeyUp, RoutingStrategies.Tunnel, true);
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

    private async void OnSubtitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || disposed || e.Handled)
        {
            return;
        }

        if (enterPressed)
        {
            e.Handled = true;
            return;
        }

        if (e.Source is not TextBox { AcceptsReturn: true, DataContext: SubtitleRow row } box ||
            box.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)))
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Shift)
        {
            return;
        }

        e.Handled = true;
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        enterPressed = true;
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

    private void OnSubtitleKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && enterPressed)
        {
            enterPressed = false;
            e.Handled = true;
        }
    }

    private void OnKeyboardRootDeactivated(object? sender, EventArgs e)
    {
        enterPressed = false;
        rowNavigationRevision++;
    }

    private void ReleaseKeyboardRoot()
    {
        rowNavigationRevision++;
        enterPressed = false;
        if (keyboardRoot is { } root)
        {
            root.RemoveHandler(KeyUpEvent, OnSubtitleKeyUp);
            if (root is Window window)
            {
                window.Deactivated -= OnKeyboardRootDeactivated;
            }
            keyboardRoot = null;
        }
    }
}
