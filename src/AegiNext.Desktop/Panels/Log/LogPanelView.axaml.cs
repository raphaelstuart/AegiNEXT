using AegiNext.Desktop.Workspace.Diagnostics;
using AegiNext.Desktop.Layouts;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Log;

internal sealed partial class LogPanelView : UserControl, IWorkbenchPanelView
{
    private readonly LogPanelViewModel viewModel;
    private readonly WorkbenchLogJournal journal;
    private readonly Func<bool> isClosing;
    private readonly Func<string, Task> writeClipboard;
    private bool disposed;

    internal LogPanelView(LogPanelViewModel viewModel, WorkbenchLogJournal journal, Func<bool>? isClosing = null,
        Func<string, Task>? writeClipboard = null)
    {
        this.viewModel = viewModel;
        this.journal = journal;
        this.isClosing = isClosing ?? (() => false);
        this.writeClipboard = writeClipboard ?? WriteClipboardAsync;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        viewModel.SetReadingState(IsReading);
        viewModel.PropertyChanged += OnModelChanged;
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        LayoutUpdated += OnLayoutUpdated;
    }

    public string PanelId => WorkbenchPanelIds.LOG;
    internal Task CopyCompletion { get; private set; } = Task.CompletedTask;
    public void CancelGestures()
    {
    }
    public void FocusInvalidField(string? fieldKey) => this.FindControl<TextBox>("LogSearch")!.Focus();

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        IsEnabled = false;
        viewModel.SetReadingState(null);
        viewModel.PropertyChanged -= OnModelChanged;
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        LayoutUpdated -= OnLayoutUpdated;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => RefreshReading();
    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => RefreshReading();
    private void OnLayoutUpdated(object? sender, EventArgs e) => RefreshReading();

    private void RefreshReading()
    {
        viewModel.MarkVisibleEntriesRead();
    }

    private bool IsReading() => this.IsAttachedToVisualTree() && IsEffectivelyVisible && Bounds.Width > 0 && Bounds.Height > 0;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPanelViewModel.FilterIndex))
        {
            this.FindControl<ComboBox>("LogLevelFilter")!.SetCurrentValue(ComboBox.SelectedIndexProperty, viewModel.FilterIndex);
        }
    }

    private async void CopySelected(object? sender, RoutedEventArgs e)
    {
        CopyCompletion = CopyAsync(viewModel.Details);
        await CopyCompletion;
    }

    private async void CopyVisible(object? sender, RoutedEventArgs e)
    {
        CopyCompletion = CopyAsync(viewModel.CopyVisibleText());
        await CopyCompletion;
    }

    private async Task WriteClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable.");
        await clipboard.SetTextAsync(text);
    }

    private async Task CopyAsync(string text)
    {
        if (disposed || isClosing())
        {
            return;
        }
        try
        {
            await writeClipboard(text);
        }
        catch (Exception error)
        {
            if (!disposed && !isClosing())
            {
                journal.ReportError("Clipboard", error);
            }
        }
    }
}
