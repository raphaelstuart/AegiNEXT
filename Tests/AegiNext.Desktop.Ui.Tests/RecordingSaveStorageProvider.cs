using System.Reflection;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>记录保存选项与初始目录，并模拟取消选择文件。</summary>
public class RecordingSaveStorageProvider : DispatchProxy
{
    internal FilePickerSaveOptions? Options { get; private set; }
    internal IStorageFolder? Folder { get; set; }
    internal Uri? RequestedFolderPath { get; private set; }

    /// <inheritdoc />
    protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IStorageProvider.TryGetFolderFromPathAsync))
        {
            RequestedFolderPath = (Uri)args![0]!;
            return Task.FromResult(Folder);
        }
        if (targetMethod?.Name == nameof(IStorageProvider.SaveFilePickerAsync))
        {
            Options = (FilePickerSaveOptions)args![0]!;
            return Task.FromResult<IStorageFile?>(null);
        }
        throw new NotSupportedException(targetMethod?.Name);
    }
}
