using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Controls.Selection;
using System.ComponentModel;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetManagerWindow : Window, IDisposable
{
    private readonly List<IDisposable> localizationBindings = [];
    private readonly LayoutPresetManagerViewModel viewModel;
    private readonly ListBox list;
    private bool synchronizingSelection;
    private bool disposed;

    internal LayoutPresetManagerWindow(WorkbenchLayoutController controller)
    {
        var dialogs = new WindowWorkbenchDialogService(this);
        viewModel = new(controller, dialogs.ConfirmPresetDeletionAsync);
        DataContext = viewModel;
        localizationBindings.Add(this.Bind(TitleProperty, ObserveTitle().ToBinding()));
        Width = 540;
        Height = 430;
        MinWidth = 420;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        list = new() { Name = "LayoutPresetList", ItemsSource = viewModel.Presets, MinHeight = 120, SelectionMode = SelectionMode.Multiple };
        list.Bind(IsEnabledProperty, new Binding(nameof(viewModel.CanEdit)));
        list.SelectionChanged += OnSelectionChanged;
        viewModel.ChoicesRefreshing += OnChoicesRefreshing;
        viewModel.ChoicesRefreshed += OnChoicesRefreshed;
        viewModel.PropertyChanged += OnModelPropertyChanged;
        SynchronizeSelection();
        var name = new TextBox();
        localizationBindings.Add(name.Bind(TextBox.PlaceholderTextProperty, Localization.Observe("Layout.Name").ToBinding()));
        name.Bind(TextBox.TextProperty, new Binding(nameof(viewModel.Name)) { Mode = BindingMode.TwoWay });
        name.Bind(IsEnabledProperty, new Binding(nameof(viewModel.CanEdit)));
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(viewModel.Error)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new Button { Command = viewModel.ApplyCommand };
        localizationBindings.Add(apply.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Apply").ToBinding()));
        var rename = new Button { Command = viewModel.RenameCommand };
        localizationBindings.Add(rename.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Rename").ToBinding()));
        var delete = new Button { Name = "LayoutDeleteButton", Command = viewModel.DeleteCommand };
        localizationBindings.Add(delete.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Delete").ToBinding()));
        buttons.Children.Add(apply);
        buttons.Children.Add(rename);
        buttons.Children.Add(delete);
        var saveAs = new Button { Command = viewModel.SaveAsCommand };
        localizationBindings.Add(saveAs.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.SaveAs").ToBinding()));
        var close = new Button { HorizontalAlignment = HorizontalAlignment.Right };
        localizationBindings.Add(close.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Close").ToBinding()));
        close.Click += (_, _) => Close();
        var body = new Grid { Margin = new(16), RowDefinitions = new("*,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 12 };
        body.Children.Add(list);
        Grid.SetRow(name, 1);
        body.Children.Add(name);
        Grid.SetRow(buttons, 2);
        body.Children.Add(buttons);
        Grid.SetRow(saveAs, 3);
        body.Children.Add(saveAs);
        Grid.SetRow(error, 4);
        body.Children.Add(error);
        Grid.SetRow(close, 5);
        body.Children.Add(close);
        Content = body;
        Closed += OnClosed;
    }

    private static IObservable<string> ObserveTitle()
    {
        return Localization.Observe(() => Localization.Get("Layout.Manage").TrimEnd('…'));
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!synchronizingSelection)
        {
            var ids = list.SelectedItems?.OfType<LayoutPresetRow>().Select(row => row.Id).ToArray() ?? [];
            var primary = e.AddedItems.OfType<LayoutPresetRow>().LastOrDefault()?.Id
                ?? (viewModel.Selected is { } current && ids.Contains(current.Id) ? current.Id : ids.FirstOrDefault());
            viewModel.SetSelection(primary, ids);
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!synchronizingSelection && e.PropertyName == nameof(LayoutPresetManagerViewModel.SelectedIds))
        {
            SynchronizeSelection();
        }
    }

    private void OnChoicesRefreshing(object? sender, EventArgs e)
    {
        synchronizingSelection = true;
    }

    private void OnChoicesRefreshed(object? sender, EventArgs e)
    {
        synchronizingSelection = false;
        SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        if ((list.SelectedItems?.OfType<LayoutPresetRow>().Select(row => row.Id) ?? [])
            .ToHashSet(StringComparer.Ordinal).SetEquals(viewModel.SelectedIds))
        {
            return;
        }
        synchronizingSelection = true;
        try
        {
            list.SelectedItems?.Clear();
            foreach (var row in viewModel.Presets.Where(row => viewModel.SelectedIds.Contains(row.Id)))
            {
                list.SelectedItems?.Add(row);
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        list.SelectionChanged -= OnSelectionChanged;
        viewModel.ChoicesRefreshing -= OnChoicesRefreshing;
        viewModel.ChoicesRefreshed -= OnChoicesRefreshed;
        viewModel.PropertyChanged -= OnModelPropertyChanged;
        Closed -= OnClosed;
        foreach (var binding in localizationBindings)
        {
            binding.Dispose();
        }

        localizationBindings.Clear();
        viewModel.Dispose();
    }
}
