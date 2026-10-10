using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.ColorTags;

/// <summary>串行保存个人常用标记，磁盘提交成功后才发布新快照。</summary>
public sealed class SubtitleColorTagLibrary : IDisposable
{
    private readonly string filePath;
    private readonly ImmutableArray<SubtitleColorTag> defaults;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private SubtitleColorTagLibraryDocument snapshot = new();
    private bool loaded;
    private bool disposed;
    private int operations;

    /// <summary>绑定宿主注入的本机绝对路径及仅在缺文件时使用的默认定义。</summary>
    public SubtitleColorTagLibrary(string absoluteFilePath, ImmutableArray<SubtitleColorTag> defaults = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
        {
            throw new ArgumentException("颜色标记库路径必须为本机绝对路径。", nameof(absoluteFilePath));
        }
        this.defaults = defaults.IsDefault ? [] : defaults;
        SubtitleColorTagValidator.Validate(this.defaults);
        filePath = Path.GetFullPath(absoluteFilePath);
    }

    public SubtitleColorTagLibraryDocument Snapshot => Volatile.Read(ref snapshot);

    /// <summary>加载完整库；仅缺文件时将构造时的默认定义原子保存，已有空库保持为空。</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return LoadAsync(defaults, cancellationToken);
    }

    /// <summary>加载完整库，允许宿主在确定启动语言后提供仅用于缺文件初始化的默认定义。</summary>
    public async Task LoadAsync(ImmutableArray<SubtitleColorTag> defaultTags, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadAsync(defaultTags, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>整体替换个人库；提交失败保持磁盘和已发布快照，内容相同不重复写入。</summary>
    public async Task ReplaceAsync(SubtitleColorTagLibraryDocument document, Action? beforeCommit = null,
        CancellationToken cancellationToken = default)
    {
        SubtitleColorTagStore.Validate(document);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (loaded && snapshot.Tags.SequenceEqual(document.Tags))
            {
                return;
            }
            await CommitAsync(document, beforeCommit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>按稳定身份新增或更新个人定义，不修改任何工程快照。</summary>
    public async Task UpsertAsync(SubtitleColorTag tag, CancellationToken cancellationToken = default)
    {
        SubtitleColorTagValidator.Validate(tag);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = FindIndex(tag.Id);
            if (index >= 0 && snapshot.Tags[index] == tag)
            {
                return;
            }
            var tags = index < 0 ? snapshot.Tags.Add(tag) : snapshot.Tags.SetItem(index, tag);
            await CommitAsync(snapshot with { Tags = tags }, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>删除个人库中的指定标记，已保存工程中的独立定义保持原样。</summary>
    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = FindIndex(id);
            if (index >= 0)
            {
                await CommitAsync(snapshot with { Tags = snapshot.Tags.RemoveAt(index) }, null, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Exit();
        }
    }

    /// <inheritdoc />
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
                throw new InvalidOperationException("颜色标记库必须在操作完成后释放。");
            }
            disposed = true;
            gate.Dispose();
        }
    }

    private Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        return loaded ? Task.CompletedTask : ReadAsync(defaults, cancellationToken);
    }

    private async Task ReadAsync(ImmutableArray<SubtitleColorTag> defaultTags, CancellationToken cancellationToken)
    {
        SubtitleColorTagLibraryDocument document;
        try
        {
            document = await SubtitleColorTagStore.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            document = new() { Tags = defaultTags.IsDefault ? [] : defaultTags };
            await SubtitleColorTagStore.SaveAsync(document, filePath, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref snapshot, document);
        loaded = true;
    }

    private async Task CommitAsync(SubtitleColorTagLibraryDocument document, Action? beforeCommit, CancellationToken cancellationToken)
    {
        await SubtitleColorTagStore.SaveAsync(document, filePath, beforeCommit, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref snapshot, document);
        loaded = true;
    }

    private int FindIndex(Guid id)
    {
        for (var index = 0; index < snapshot.Tags.Length; index++)
        {
            if (snapshot.Tags[index].Id == id)
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
        lock (lifetime)
        {
            gate.Release();
            operations--;
        }
    }
}
