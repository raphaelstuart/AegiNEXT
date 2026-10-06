using System.Reflection;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>记录本地目录选择请求并模拟取消。</summary>
public class RecordingFolderStorageProvider : DispatchProxy
{
    internal FolderPickerOpenOptions? Options { get; private set; }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IStorageProvider.OpenFolderPickerAsync))
        {
            Options = (FolderPickerOpenOptions)args![0]!;
            return Task.FromResult<IReadOnlyList<IStorageFolder>>([]);
        }
        throw new NotSupportedException(targetMethod?.Name);
    }
}
