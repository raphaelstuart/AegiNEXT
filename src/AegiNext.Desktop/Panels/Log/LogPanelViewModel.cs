using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Log;

internal sealed class LogPanelViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchLogJournal journal;
    private int filterIndex;
    private string filterText = string.Empty;
    private WorkbenchLogEntry? selectedEntry;
    private Func<bool>? readingState;
    private bool disposed;

    internal LogPanelViewModel(WorkbenchLogJournal journal)
    {
        this.journal = journal;
        ClearCommand = new(journal.Clear);
        journal.Changed += OnJournalChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        RefreshLanguage();
        Refresh();
    }

    public RelayCommand ClearCommand { get; }
    public IReadOnlyList<WorkbenchLogEntry> Entries { get; private set; } = [];
    public IReadOnlyList<string> Levels { get; private set; } = [];
    public string SearchLabel { get; private set; } = string.Empty;
    public string CopySelectedLabel { get; private set; } = string.Empty;
    public string CopyVisibleLabel { get; private set; } = string.Empty;
    public string ClearLabel { get; private set; } = string.Empty;
    public string EmptyLabel { get; private set; } = string.Empty;
    public bool IsEmpty => Entries.Count == 0;
    public bool HasSelection => SelectedEntry is not null;
    public int UnreadErrorCount => journal.UnreadErrorCount;
    public string Details => SelectedEntry?.FullText ?? string.Empty;

    public int FilterIndex
    {
        get => filterIndex;
        set
        {
            if (value is >= 0 and <= 3 && SetProperty(ref filterIndex, value))
            {
                Refresh();
            }
        }
    }

    public string FilterText
    {
        get => filterText;
        set
        {
            if (SetProperty(ref filterText, value))
            {
                Refresh();
            }
        }
    }

    public WorkbenchLogEntry? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                OnPropertyChanged(nameof(Details));
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    internal string CopyVisibleText() => string.Join(Environment.NewLine + Environment.NewLine, Entries.Select(entry => entry.FullText));

    internal void RevealEntry(WorkbenchLogEntry entry)
    {
        FilterIndex = 0;
        FilterText = string.Empty;
        SelectedEntry = entry;
    }

    internal void SetReadingState(Func<bool>? value)
    {
        readingState = value;
        MarkVisibleEntriesRead();
    }

    internal void MarkVisibleEntriesRead()
    {
        if (!disposed && readingState?.Invoke() == true)
        {
            journal.MarkRead();
        }
    }

    internal void RefreshLanguage()
    {
        Levels = [Localization.Get("Log.All"), Localization.Get("Log.Info"), Localization.Get("Log.Warning"), Localization.Get("Log.Error")];
        SearchLabel = Localization.Get("Log.Search");
        CopySelectedLabel = Localization.Get("Log.CopySelected");
        CopyVisibleLabel = Localization.Get("Log.CopyVisible");
        ClearLabel = Localization.Get("Log.Clear");
        EmptyLabel = Localization.Get("Log.Empty");
        foreach (var name in new[] { nameof(Levels), nameof(SearchLabel), nameof(CopySelectedLabel), nameof(CopyVisibleLabel), nameof(ClearLabel), nameof(EmptyLabel), nameof(FilterIndex) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        disposed = true;
        journal.Changed -= OnJournalChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLanguage();

    private void OnJournalChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        if (disposed)
        {
            return;
        }

        var selected = SelectedEntry;
        Entries = journal.Entries.Where(entry =>
            (FilterIndex == 0 || entry.Level == (WorkbenchLogLevel)(FilterIndex - 1)) &&
            (string.IsNullOrWhiteSpace(FilterText) || entry.FullText.Contains(FilterText, StringComparison.OrdinalIgnoreCase))).ToArray();
        OnPropertyChanged(nameof(Entries));
        SelectedEntry = selected is not null && Entries.Contains(selected) ? selected : null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(UnreadErrorCount));
        MarkVisibleEntriesRead();
    }
}
