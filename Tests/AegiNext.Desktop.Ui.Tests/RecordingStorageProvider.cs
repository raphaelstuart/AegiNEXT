using System.Reflection;
using Avalonia.Platform.Storage;

namespace AegiNext.Desktop.Ui.Tests;

public class RecordingStorageProvider : DispatchProxy
{
    internal FilePickerOpenOptions? OpenOptions { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IStorageProvider.OpenFilePickerAsync))
        {
            OpenOptions = (FilePickerOpenOptions)args![0]!;
            return Task.FromResult<IReadOnlyList<IStorageFile>>([]);
        }
        throw new NotSupportedException(targetMethod?.Name);
    }
}
