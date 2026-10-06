using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
            input.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (DataContext is ProjectSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                    !input.GetVisualDescendants().OfType<TextPresenter>()
                        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)))
                {
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
                if (IsEffectivelyVisible && DataContext is ProjectSettingsViewModel model)
                {
                    model.Commit(field);
                }
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
