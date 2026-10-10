using System.ComponentModel;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Subtitles;

internal sealed partial class SubtitlesPanelView
{
    private readonly SubtitleColorTagMenu colorTagMenu = new("SubtitleRowsColorTagMenuItem");
    private readonly HashSet<ListBoxItem> colorTagContainers = [];
    private bool synchronizingFilter;
    private ComboBox colorTagFilterCombo = null!;

    private void InitializeColorTags()
    {
        contextMenu.Items.Add(colorTagMenu.Item);
        colorTagFilterCombo = this.FindControl<ComboBox>("SubtitleColorTagFilterCombo")!;
        colorTagFilterCombo.SelectionChanged += OnColorTagFilterChanged;
        list.ContainerPrepared += OnColorTagContainerPrepared;
        list.ContainerClearing += OnColorTagContainerClearing;
        viewModel.PropertyChanged += OnColorTagViewModelChanged;
        ActualThemeVariantChanged += OnColorTagThemeChanged;
    }

    private void RefreshColorTagMenu()
    {
        var document = session.DocumentSnapshot;
        var ids = viewModel.SelectedIds.ToArray();
        var selected = document.Subtitles.Where(line => ids.Contains(line.Id)).Select(line => line.ColorTagId).Distinct().ToArray();
        colorTagMenu.Refresh(document.ColorTags, session.ApplicationContext.ColorTagLibrary.Snapshot.Tags,
            selected.FirstOrDefault(), selected.Length == 1,
            () => ids.Length > 0 && ReferenceEquals(document, session.DocumentSnapshot) && !session.IsClosing && !session.IsProjectBusy,
            tagId => session.SetSubtitleColorTagAsync(ids, tagId, document),
            template => session.ApplySubtitleColorTagAsync(ids, template, document), session.OpenSubtitleColorTagSettingsAsync);
    }

    private void OnColorTagFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingFilter || session.IsUpdating || disposed || colorTagFilterCombo.SelectedItem is not SubtitleColorTagFilterChoice choice)
        {
            return;
        }
        synchronizingFilter = true;
        try
        {
            viewModel.SelectColorTagFilter(choice);
            colorTagFilterCombo.SelectedItem = viewModel.SelectedColorTagFilter;
        }
        finally
        {
            synchronizingFilter = false;
        }
        SynchronizeSelection();
    }

    private void OnColorTagContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem item && colorTagContainers.Add(item))
        {
            item.PropertyChanged += OnColorTagContainerChanged;
            item.Loaded += OnColorTagContainerLoaded;
        }
        RefreshColorTagContainers();
    }

    private void OnColorTagContainerLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RefreshColorTagContainers();

    private void OnColorTagContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is ListBoxItem item)
        {
            item.PropertyChanged -= OnColorTagContainerChanged;
            item.Loaded -= OnColorTagContainerLoaded;
            colorTagContainers.Remove(item);
        }
    }

    private void OnColorTagContainerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ListBoxItem.IsSelectedProperty || e.Property == DataContextProperty)
        {
            RefreshColorTagContainers();
        }
    }

    private void OnColorTagViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SubtitlesPanelViewModel.VisibleRows) or nameof(SubtitlesPanelViewModel.SelectedIds) or
            nameof(SubtitlesPanelViewModel.ColorTagFilters) or nameof(SubtitlesPanelViewModel.SelectedColorTagFilter))
        {
            RefreshColorTagContainers();
        }
    }

    private void OnColorTagThemeChanged(object? sender, EventArgs e) => RefreshColorTagContainers();

    private void RefreshColorTagContainers()
    {
        var tags = session.DocumentSnapshot.ColorTags.ToDictionary(tag => tag.Id);
        foreach (var container in colorTagContainers)
        {
            var row = container.DataContext as SubtitleRow ?? container.Content as SubtitleRow;
            var color = row?.ColorTagId is { } tagId && tags.TryGetValue(tagId, out var tag) ? tag.ColorHex : null;
            var background = color is null ? (IBrush)Brushes.Transparent :
                SubtitleColorTagPalette.ResolveRowBackground(color, ActualThemeVariant == ThemeVariant.Dark, container.IsSelected);
            var surface = container.GetVisualDescendants().OfType<Grid>().FirstOrDefault(grid => grid.Name == "SubtitleRowSurface");
            surface?.SetCurrentValue(Panel.BackgroundProperty, background);
        }
    }

    private void ReleaseColorTags()
    {
        colorTagFilterCombo.SelectionChanged -= OnColorTagFilterChanged;
        list.ContainerPrepared -= OnColorTagContainerPrepared;
        list.ContainerClearing -= OnColorTagContainerClearing;
        viewModel.PropertyChanged -= OnColorTagViewModelChanged;
        ActualThemeVariantChanged -= OnColorTagThemeChanged;
        foreach (var container in colorTagContainers)
        {
            container.PropertyChanged -= OnColorTagContainerChanged;
            container.Loaded -= OnColorTagContainerLoaded;
        }
        colorTagContainers.Clear();
        colorTagMenu.Dispose();
    }
}
