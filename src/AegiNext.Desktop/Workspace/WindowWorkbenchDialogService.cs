using AegiNext.Application;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed class WindowWorkbenchDialogService : IWorkbenchDialogService
{
    private readonly Func<Window> ownerProvider;
    private readonly Func<IStorageProvider>? storageProvider;
    private readonly Action<Window>? registerWindow;

    internal WindowWorkbenchDialogService(Window owner, Func<IStorageProvider>? storageProvider = null,
        Action<Window>? registerWindow = null) : this(() => owner, storageProvider, registerWindow)
    {
    }

    internal WindowWorkbenchDialogService(Func<Window> ownerProvider, Func<IStorageProvider>? storageProvider = null,
        Action<Window>? registerWindow = null)
    {
        this.ownerProvider = ownerProvider;
        this.storageProvider = storageProvider;
        this.registerWindow = registerWindow;
    }
    /// <summary>在所属窗口选择本地文件；取消时返回空路径。</summary>
    public async Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        var files = await (storageProvider?.Invoke() ?? ownerProvider().StorageProvider).OpenFilePickerAsync(new()
        {
            Title = Localization.Get("Workbench." + (title)), AllowMultiple = false,
            FileTypeFilter = [new(Localization.Get("Workbench." + (typeName))) { Patterns = patterns }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
    }

    /// <summary>选择带指定扩展名的本地保存路径，并由系统确认覆盖。</summary>
    public async Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName)
    {
        var file = await (storageProvider?.Invoke() ?? ownerProvider().StorageProvider).SaveFilePickerAsync(new()
        {
            Title = Localization.Get("Workbench." + (title)),
            SuggestedFileName = suggestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? suggestedName[..^extension.Length] : suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new(Localization.Get("Workbench." + (typeName))) { Patterns = patterns }], ShowOverwritePrompt = true
        });
        return file is null ? null : file.TryGetLocalPath() ?? throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
    }

    /// <summary>填写项目创建信息；错误留在同一面板中，成功后才关闭。</summary>
    public async Task<bool> ShowNewProjectAsync(string workspaceRoot,
        Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NewProjectDialog? dialog = null;
        using var model = new NewProjectDialogViewModel(workspaceRoot, create, async token =>
        {
            token.ThrowIfCancellationRequested();
            var folders = await (storageProvider?.Invoke() ?? dialog!.StorageProvider).OpenFolderPickerAsync(new()
            {
                Title = Localization.Get("Workbench.BrowseProjectLocation"), AllowMultiple = false
            });
            token.ThrowIfCancellationRequested();
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath()
                ?? throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
        }, cancellationToken);
        dialog = new(model);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        var accepted = false;
        try
        {
            accepted = await answer;
        }
        finally
        {
            model.Dispose();
            await model.Completion;
        }
        return accepted || model.WasCreated;
    }

    /// <summary>展示具体字幕转换损失，并等待用户明确继续或取消。</summary>
    public Task<bool> ConfirmSubtitleConversionAsync(IReadOnlyList<string> diagnostics)
    {
        var dialog = new SubtitleConversionDialog(diagnostics);
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<bool>(ownerProvider());
    }

    /// <summary>等待用户决定如何处理项目的未保存修改。</summary>
    public Task<int> ConfirmUnsavedAsync()
    {
        var dialog = new UnsavedProjectDialog();
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<int>(ownerProvider());
    }

    /// <summary>确认是否在媒体不可用时继续打开项目；取消请求会关闭确认窗口。</summary>
    public async Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new UnavailableProjectMediaDialog(mediaPath, reason);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }

    /// <summary>决定是否将更换的轨道预设同步到现有片段；默认仅更新后续创建样式。</summary>
    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
    {
        var dialog = new TrackStyleChangeDialog(trackName, presetName, subtitleCount);
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<TrackStyleUpdateDecision>(ownerProvider());
    }

    /// <summary>确认删除字幕轨道及全部内容；取消请求会关闭窗口，默认选择取消。</summary>
    public async Task<bool> ConfirmTrackDeletionAsync(string trackName, int subtitleCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new TrackDeletionDialog(trackName, subtitleCount);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }
}
