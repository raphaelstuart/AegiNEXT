using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Workspace;

internal sealed class WindowWorkbenchDialogService(Window owner) : IWorkbenchDialogService
{
    /// <summary>在所属窗口选择本地文件；取消时返回空路径。</summary>
    public async Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = WorkbenchText.Get(title), AllowMultiple = false,
            FileTypeFilter = [new(WorkbenchText.Get(typeName)) { Patterns = patterns }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new NotSupportedException(PreviewText.Get("LocalFile", System.Globalization.CultureInfo.CurrentUICulture));
    }

    /// <summary>选择带指定扩展名的本地保存路径，并由系统确认覆盖。</summary>
    public async Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = WorkbenchText.Get(title),
            SuggestedFileName = suggestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? suggestedName[..^extension.Length] : suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new(WorkbenchText.Get(typeName)) { Patterns = patterns }], ShowOverwritePrompt = true
        });
        return file is null ? null : file.TryGetLocalPath() ?? throw new NotSupportedException(PreviewText.Get("LocalFile", System.Globalization.CultureInfo.CurrentUICulture));
    }

    /// <summary>等待用户决定如何处理工程的未保存修改。</summary>
    public Task<int> ConfirmUnsavedAsync() => new UnsavedProjectDialog().ShowDialog<int>(owner);
}
