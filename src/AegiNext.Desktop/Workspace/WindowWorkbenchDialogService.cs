using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Workspace;

internal sealed class WindowWorkbenchDialogService(Window owner, Func<IStorageProvider>? storageProvider = null,
    Action<Window>? registerWindow = null) : IWorkbenchDialogService
{
    /// <summary>在所属窗口选择本地文件；取消时返回空路径。</summary>
    public async Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        var files = await (storageProvider?.Invoke() ?? owner.StorageProvider).OpenFilePickerAsync(new()
        {
            Title = WorkbenchText.Get(title), AllowMultiple = false,
            FileTypeFilter = [new(WorkbenchText.Get(typeName)) { Patterns = patterns }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new NotSupportedException(PreviewText.Get("LocalFile", System.Globalization.CultureInfo.CurrentUICulture));
    }

    /// <summary>选择带指定扩展名的本地保存路径，并由系统确认覆盖。</summary>
    public async Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName)
    {
        var file = await (storageProvider?.Invoke() ?? owner.StorageProvider).SaveFilePickerAsync(new()
        {
            Title = WorkbenchText.Get(title),
            SuggestedFileName = suggestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? suggestedName[..^extension.Length] : suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new(WorkbenchText.Get(typeName)) { Patterns = patterns }], ShowOverwritePrompt = true
        });
        return file is null ? null : file.TryGetLocalPath() ?? throw new NotSupportedException(PreviewText.Get("LocalFile", System.Globalization.CultureInfo.CurrentUICulture));
    }

    /// <summary>等待用户决定如何处理工程的未保存修改。</summary>
    public Task<int> ConfirmUnsavedAsync()
    {
        var dialog = new UnsavedProjectDialog();
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<int>(owner);
    }

    /// <summary>决定是否将更换的轨道预设同步到现有片段；默认仅更新后续创建样式。</summary>
    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
    {
        var dialog = new TrackStyleChangeDialog(trackName, presetName, subtitleCount);
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<TrackStyleUpdateDecision>(owner);
    }
}
