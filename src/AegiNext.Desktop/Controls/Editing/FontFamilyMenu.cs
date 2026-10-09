using System.Collections;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Utils;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

internal sealed class FontFamilyMenu : ContextMenu, ISelectionAdapter
{
    private readonly Func<FontSelection> currentFont;
    private IEnumerable? candidates;
    private INotifyCollectionChanged? observableCandidates;
    private FontPickerCandidate? selectedCandidate;
    private FontSelection[] displayedSelections = [];
    private FontSelection? displayedCurrent;
    private IFontNamePreviewProvider? previewProvider;
    private IFontNamePreviewProvider? displayedPreviewProvider;
    private event EventHandler<SelectionChangedEventArgs>? CandidateSelectionChanged;
    private bool refreshPending;
    private bool committing;
    private bool disposed;

    internal FontFamilyMenu(Func<FontSelection> currentFont)
    {
        this.currentFont = currentFont;
        Name = "FontFamilyMenu";
        AddHandler(MenuItem.ClickEvent, OnCandidateClick);
    }

    internal bool ContainsKeyboardFocus => TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement() is Control focused &&
        (ReferenceEquals(this, focused) || this.IsLogicalAncestorOf(focused));

    internal FontPickerCandidate? CommittedCandidate { get; private set; }

    internal IFontNamePreviewProvider? PreviewProvider
    {
        get => previewProvider;
        set
        {
            if (ReferenceEquals(previewProvider, value))
            {
                return;
            }
            previewProvider = value;
            QueueRefresh();
        }
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ContextMenu);

    object? ISelectionAdapter.SelectedItem
    {
        get => selectedCandidate;
        set => selectedCandidate = value as FontPickerCandidate;
    }

    IEnumerable? ISelectionAdapter.ItemsSource
    {
        get => candidates;
        set
        {
            if (observableCandidates is not null)
            {
                observableCandidates.CollectionChanged -= OnCandidatesChanged;
            }
            candidates = value;
            observableCandidates = value as INotifyCollectionChanged;
            if (observableCandidates is not null)
            {
                observableCandidates.CollectionChanged += OnCandidatesChanged;
            }
            QueueRefresh();
        }
    }

    event EventHandler<SelectionChangedEventArgs>? ISelectionAdapter.SelectionChanged
    {
        add => CandidateSelectionChanged += value;
        remove => CandidateSelectionChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<RoutedEventArgs>? Commit;

    /// <inheritdoc />
    public event EventHandler<RoutedEventArgs>? Cancel;

    /// <inheritdoc />
    public override void Open()
    {
        RefreshItems();
        IsOpen = true;
        RaiseEvent(new(OpenedEvent));
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (!IsOpen)
        {
            return;
        }
        Dismiss();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.Source is MenuItem { Parent: FontFamilyMenu } or FontFamilyMenu)
        {
            Cancel?.Invoke(this, new());
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    internal void Dismiss()
    {
        DismissSubmenus();
        SelectedIndex = -1;
        if (IsOpen)
        {
            IsOpen = false;
            RaiseEvent(new(ClosedEvent));
        }
    }

    /// <inheritdoc />
    public void HandleKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when !e.KeyModifiers.HasFlag(KeyModifiers.Alt):
            case Key.Up:
                if (ItemCount > 0)
                {
                    SelectedIndex = e.Key == Key.Down ? (SelectedIndex + 1) % ItemCount :
                        (SelectedIndex < 0 ? ItemCount - 1 : (SelectedIndex + ItemCount - 1) % ItemCount);
                    (SelectedItem as Control)?.BringIntoView();
                }
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Enter:
                if (SelectedItem is MenuItem item)
                {
                    if (item.HasSubMenu)
                    {
                        item.IsSubMenuOpen = true;
                        item.Items.OfType<MenuItem>().FirstOrDefault()?.Focus(NavigationMethod.Directional);
                    }
                    else if (e.Key == Key.Enter && item.Tag is FontPickerCandidate candidate)
                    {
                        CommitCandidate(candidate);
                    }
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    Commit?.Invoke(this, new());
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                Cancel?.Invoke(this, new());
                e.Handled = true;
                break;
        }
    }

    internal void Release()
    {
        disposed = true;
        Dismiss();
        ReleasePresenters();
        if (observableCandidates is not null)
        {
            observableCandidates.CollectionChanged -= OnCandidatesChanged;
        }
        observableCandidates = null;
        candidates = null;
        RemoveHandler(MenuItem.ClickEvent, OnCandidateClick);
    }

    private void OnCandidatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (refreshPending || disposed)
        {
            return;
        }
        refreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            refreshPending = false;
            if (!disposed && IsOpen && !committing)
            {
                RefreshItems();
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshItems()
    {
        var current = currentFont();
        var values = (candidates?.OfType<FontPickerCandidate>() ?? []).ToArray();
        var selections = values.Select(candidate => candidate.Selection).ToArray();
        if (displayedCurrent == current && displayedSelections.SequenceEqual(selections) &&
            ReferenceEquals(displayedPreviewProvider, PreviewProvider))
        {
            return;
        }
        var families = values
            .GroupBy(candidate => candidate.Selection.FamilyName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(family => string.Equals(family.Key, current.FamilyName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(family => family.Key, StringComparer.CurrentCultureIgnoreCase);
        DismissSubmenus();
        ReleasePresenters();
        ItemsSource = families.Select(family =>
        {
            var variants = family.Where(candidate => candidate.Selection.Variant is not null)
                .OrderByDescending(candidate => FontSelectionResolver.HasSameFace(candidate.Selection, current))
                .ThenBy(candidate => candidate.Selection.Variant?.Weight)
                .ThenBy(candidate => candidate.Selection.Variant?.Width)
                .ThenBy(candidate => candidate.Selection.Variant?.Italic)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var item = new MenuItem
            {
                Header = family.Key,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = string.Equals(family.Key, current.FamilyName, StringComparison.OrdinalIgnoreCase)
            };
            var representative = variants.FirstOrDefault(candidate => FontSelectionResolver.HasSameFace(candidate.Selection, current))
                ?? variants.OrderBy(candidate => Math.Abs(candidate.Selection.Variant!.Value.Weight - 400))
                    .ThenBy(candidate => Math.Abs(candidate.Selection.Variant!.Value.Width - 5))
                    .ThenBy(candidate => candidate.Selection.Variant!.Value.Italic).FirstOrDefault()
                ?? family.First();
            item.HeaderTemplate = CreatePreviewTemplate(representative.Selection);
            if (variants.Length > 1)
            {
                item.ItemsSource = variants.Select(candidate => new MenuItem
                {
                    Header = candidate.Selection.Variant?.Name,
                    HeaderTemplate = CreatePreviewTemplate(candidate.Selection),
                    Tag = candidate,
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = FontSelectionResolver.HasSameFace(candidate.Selection, current)
                }).ToArray();
            }
            else
            {
                item.Tag = variants.Length == 1 ? variants[0] : family.First();
            }
            return item;
        }).ToArray();
        displayedSelections = selections;
        displayedCurrent = current;
        displayedPreviewProvider = PreviewProvider;
        SelectedIndex = -1;
    }

    private FuncDataTemplate<string>? CreatePreviewTemplate(FontSelection selection)
    {
        return PreviewProvider is { } provider
            ? new FuncDataTemplate<string>((name, _) => new FontNamePreviewPresenter(name, selection, provider))
            : null;
    }

    private void ReleasePresenters()
    {
        foreach (var family in Items.OfType<MenuItem>())
        {
            foreach (var item in family.Items.OfType<MenuItem>().Prepend(family))
            {
                foreach (var presenter in item.GetVisualDescendants().OfType<FontNamePreviewPresenter>())
                {
                    presenter.Dispose();
                }
            }
        }
    }

    private void DismissSubmenus()
    {
        foreach (var item in Items.OfType<MenuItem>())
        {
            item.IsSubMenuOpen = false;
        }
    }

    private void OnCandidateClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { Tag: FontPickerCandidate candidate } item)
        {
            CommitCandidate(candidate);
            item.IsChecked = FontSelectionResolver.HasSameFace(candidate.Selection, currentFont());
            e.Handled = true;
        }
    }

    private void CommitCandidate(FontPickerCandidate candidate)
    {
        committing = true;
        CommittedCandidate = candidate;
        try
        {
            var previous = selectedCandidate;
            selectedCandidate = candidate;
            CandidateSelectionChanged?.Invoke(this, new(SelectingItemsControl.SelectionChangedEvent,
                previous is null ? Array.Empty<object>() : new object[] { previous }, new object[] { candidate }));
            Commit?.Invoke(this, new());
        }
        finally
        {
            CommittedCandidate = null;
            committing = false;
        }
    }
}
