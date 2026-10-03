using System.Collections.Immutable;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>串行持久化的应用样式库；磁盘提交成功后发布新的不可变快照。</summary>
public sealed class SubtitleStylePresetLibrary : IDisposable
{
    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private SubtitleStylePresetCollection snapshot = new();
    private bool disposed;
    private bool loaded;
    private int operations;

    /// <summary>使用宿主注入的本机绝对库文件路径，不假定工作目录。</summary>
    public SubtitleStylePresetLibrary(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
        {
            throw new ArgumentException("样式库路径必须为本机绝对路径。", nameof(absoluteFilePath));
        }

        filePath = Path.GetFullPath(absoluteFilePath);
    }

    /// <summary>最后一次成功加载或提交的完整库。</summary>
    public SubtitleStylePresetCollection Snapshot => Volatile.Read(ref snapshot);

    /// <summary>完整加载并验证本地库；不存在时发布空库，损坏文件保持原快照。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>按标识新增或修改；与其他预设重名时整体拒绝。</summary>
    public async Task UpsertAsync(SubtitleStylePreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = FindIndex(preset.Id);
            var items = index < 0 ? snapshot.Presets.Add(preset) : snapshot.Presets.SetItem(index, preset);
            await CommitAsync(snapshot with { Presets = items }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>删除已存在预设，提交失败时仍保留原库。</summary>
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = FindIndex(id);
            if (index < 0)
            {
                throw new InvalidDataException("待删除的样式预设不存在。");
            }

            await CommitAsync(snapshot with { Presets = snapshot.Presets.RemoveAt(index) }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>整体导入自包含文件；任一标识、名称或载荷冲突均保留现有库。</summary>
    public async Task ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var imported = await SubtitleStylePresetStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            var combined = snapshot with { Presets = snapshot.Presets.AddRange(imported.Presets) };
            await CommitAsync(combined, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>导出全部或指定预设；不能将子集写回正在使用的库文件。</summary>
    public async Task ExportAsync(string path, IEnumerable<Guid>? ids = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var destination = Path.GetFullPath(path);
        if (string.Equals(destination, filePath, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出位置不能是正在使用的样式库文件。");
        }

        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var exported = snapshot;
            if (ids is not null)
            {
                var selection = ids.ToHashSet();
                var selected = snapshot.Presets.Where(preset => selection.Contains(preset.Id)).ToImmutableArray();
                if (selected.Length != selection.Count)
                {
                    throw new InvalidDataException("待导出的样式预设不存在。");
                }

                exported = snapshot with { Presets = selected };
            }

            await SubtitleStylePresetStore.SaveAsync(exported, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>宿主应先等待所有异步操作完成；操作尚未结束时拒绝释放以保留确定生命周期。</summary>
    public void Dispose()
    {
        lock (lifetime)
        {
            if (disposed)
            {
                return;
            }

            if (operations != 0)
            {
                throw new InvalidOperationException("请先等待样式库异步操作完成，再释放库。");
            }

            disposed = true;
            gate.Dispose();
        }
    }

    private async Task CommitAsync(SubtitleStylePresetCollection updated, CancellationToken cancellationToken)
    {
        await SubtitleStylePresetStore.SaveAsync(updated, filePath, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref snapshot, updated);
    }

    private Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        return loaded ? Task.CompletedTask : ReadAsync(cancellationToken);
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        SubtitleStylePresetCollection result;
        try
        {
            result = await SubtitleStylePresetStore.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            result = new();
        }
        catch (DirectoryNotFoundException)
        {
            result = new();
        }

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref snapshot, result);
        loaded = true;
    }

    private int FindIndex(Guid id)
    {
        for (var index = 0; index < snapshot.Presets.Length; index++)
        {
            if (snapshot.Presets[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operations++;
        }

        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (lifetime)
            {
                operations--;
            }

            throw;
        }
    }

    private void Exit()
    {
        gate.Release();
        lock (lifetime)
        {
            operations--;
        }
    }
}
