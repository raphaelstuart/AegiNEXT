using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Projects;

/// <summary>呈现项目设置并连接本地输入确认及目录选择。</summary>
public sealed partial class ProjectSettingsView : UserControl
{
    /// <summary>加载页面并绑定各字段的确认、恢复事件。</summary>
    public ProjectSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        foreach (var (name, field) in new[]
                 {
                     ("WorkspaceRootInput", ProjectSettingsField.WORKSPACE_ROOT),
                     ("AutoSaveIntervalInput", ProjectSettingsField.AUTO_SAVE_INTERVAL),
                     ("BackupIntervalInput", ProjectSettingsField.BACKUP_INTERVAL),
                     ("MaximumBackupCountInput", ProjectSettingsField.MAXIMUM_BACKUP_COUNT)
                 })
        {
            var input = this.FindControl<Control>(name)!;
            var focusRevision = 0L;
            if (input is NumericDraftInput numeric)
            {
                var title = this.FindControl<NumericDragLabel>(name + "Title")!;
                ProjectSettingsViewModel? dragModel = null;
                title.DragStarted += (_, _) =>
                {
                    focusRevision++;
                    dragModel = DataContext as ProjectSettingsViewModel;
                };
                title.DragCompleted += (_, args) =>
                {
                    focusRevision++;
                    var frozenModel = dragModel;
                    dragModel = null;
                    if (args.Changed && numeric.IsEffectivelyVisible && ReferenceEquals(DataContext, frozenModel))
                    {
                        frozenModel?.Commit(field);
                    }
                };
                title.DragCanceled += (_, _) =>
                {
                    focusRevision++;
                    dragModel = null;
                };
                DataContextChanged += (_, _) =>
                {
                    focusRevision++;
                    title.CancelDrag();
                };
                PropertyChanged += (_, args) =>
                {
                    if (args.Property == IsVisibleProperty && !IsVisible)
                    {
                        title.CancelDrag();
                    }
                };
            }
            input.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (DataContext is ProjectSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                    !input.GetVisualDescendants().OfType<TextPresenter>()
                        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)))
                {
                    focusRevision++;
                    if (args.Key == Key.Escape)
                    {
                        model.Restore(field);
                    }
                    else
                    {
                        model.Commit(field);
                    }
                    args.Handled = true;
                }
            }, RoutingStrategies.Tunnel);
            input.LostFocus += (_, _) =>
            {
                var revision = ++focusRevision;
                var currentModel = DataContext as ProjectSettingsViewModel;
                Dispatcher.UIThread.Post(() =>
                {
                    if (revision == focusRevision && input is not NumericDraftInput { IsTitleDragging: true } &&
                        !input.IsKeyboardFocusWithin && input.IsEffectivelyVisible && currentModel is not null &&
                        ReferenceEquals(DataContext, currentModel))
                    {
                        currentModel.Commit(field);
                    }
                }, DispatcherPriority.Background);
            };
        }

        this.FindControl<Button>("BrowseWorkspaceButton")!.Click += OnBrowseWorkspace;
    }

    private async void OnBrowseWorkspace(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ProjectSettingsViewModel model || TopLevel.GetTopLevel(this) is not { } owner)
        {
            return;
        }

        try
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new()
            {
                Title = Localization.Get("Workbench.BrowseProjectLocation"), AllowMultiple = false
            });
            if (folders.Count > 0 && ReferenceEquals(TopLevel.GetTopLevel(this), owner) && ReferenceEquals(DataContext, model))
            {
                model.WorkspaceRootText = folders[0].TryGetLocalPath() ??
                                          throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
                model.Commit(ProjectSettingsField.WORKSPACE_ROOT);
            }
        }
        catch (Exception error)
        {
            model.ShowError(error.Message);
        }
    }
}
