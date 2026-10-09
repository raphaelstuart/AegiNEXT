using System.Security;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed class FontNamePreviewCache : IFontNamePreviewProvider, IAsyncDisposable
{
    private const uint MAGIC = 0x504E4641;
    private const int FORMAT_VERSION = 1;
    private const int HEADER_BYTES = 36;
    private const int HASH_BYTES = 32;
    private const int MAX_PIXEL_DIMENSION = 16384;
    private const int MAX_ALPHA_BYTES = 64 * 1024 * 1024;
    private const int MEMORY_ENTRY_OVERHEAD_BYTES = 256;
    private readonly string directoryPath;
    private readonly IFontNamePreviewRenderer renderer;
    private readonly long memoryBudgetBytes;
    private readonly long diskBudgetBytes;
    private readonly Lock gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly SemaphoreSlim concurrency;
    private readonly SemaphoreSlim diskGate = new(1, 1);
    private readonly Dictionary<FontNamePreviewRequest, FontNamePreviewGeneration> pending = [];
    private readonly HashSet<FontNamePreviewGeneration> generations = [];
    private readonly Dictionary<string, (FontNamePreview Preview, LinkedListNode<string> Node, long Size)> memory = new(StringComparer.Ordinal);
    private readonly LinkedList<string> memoryOrder = new();
    private readonly Dictionary<string, (long Size, DateTime Used)> disk = new(StringComparer.Ordinal);
    private long memoryBytes;
    private long diskBytes;
    private DateTime latestDiskUse;
    private bool diskInitialized;
    private bool diskAvailable;
    private bool disposed;
    private Task? disposal;

    internal FontNamePreviewCache(string directoryPath, IFontNamePreviewRenderer renderer,
        long memoryBudgetBytes = 32 * 1024 * 1024, long diskBudgetBytes = 128 * 1024 * 1024,
        int maxConcurrentGenerations = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentOutOfRangeException.ThrowIfNegative(memoryBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(diskBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentGenerations, 1);
        this.directoryPath = directoryPath;
        this.renderer = renderer;
        this.memoryBudgetBytes = memoryBudgetBytes;
        this.diskBudgetBytes = diskBudgetBytes;
        lifetimeToken = lifetime.Token;
        concurrency = new(maxConcurrentGenerations, maxConcurrentGenerations);
    }

    /// <inheritdoc />
    public ValueTask<FontNamePreview?> GetPreviewAsync(FontNamePreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<FontNamePreview?> completion;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!pending.TryGetValue(request, out var generation))
            {
                generation = new(request);
                pending.Add(request, generation);
                generations.Add(generation);
                generation.Worker = Task.Run(() => GenerateAsync(generation), CancellationToken.None);
            }
            completion = generation.Completion.Task;
        }
        return new(completion.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is null)
            {
                disposed = true;
                var workers = generations.Select(generation => generation.Worker).ToArray();
                disposal = Task.Run(() => DisposeCoreAsync(workers));
            }
            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(Task[] workers)
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(workers).ConfigureAwait(false);
        lock (gate)
        {
            memory.Clear();
            memoryOrder.Clear();
            memoryBytes = 0;
        }
        disk.Clear();
        concurrency.Dispose();
        diskGate.Dispose();
        lifetime.Dispose();
    }

    private async Task GenerateAsync(FontNamePreviewGeneration generation)
    {
        var acquired = false;
        var cancelled = false;
        FontNamePreview? result = null;
        Exception? failure = null;
        try
        {
            var token = lifetimeToken;
            await concurrency.WaitAsync(token).ConfigureAwait(false);
            acquired = true;
            token.ThrowIfCancellationRequested();
            using var face = renderer.Resolve(generation.Request, token);
            if (face is null)
            {
                return;
            }
            var key = GetCacheKey(generation.Request, face.Fingerprint);
            if (TryGetMemory(key, out var remembered))
            {
                await TouchDiskAsync(key, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                result = remembered;
                return;
            }
            var preview = await ReadDiskAsync(key, token).ConfigureAwait(false);
            if (preview is null)
            {
                preview = face.Render(generation.Request, token);
                if (preview is not null)
                {
                    await WriteDiskAsync(key, preview, token).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
            if (preview is not null)
            {
                Remember(key, preview);
            }
            result = preview;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (acquired)
            {
                concurrency.Release();
            }
            lock (gate)
            {
                pending.Remove(generation.Request);
                if (cancelled)
                {
                    generation.Completion.TrySetCanceled(lifetimeToken);
                }
                else if (failure is not null)
                {
                    generation.Completion.TrySetException(failure);
                }
                else
                {
                    generation.Completion.TrySetResult(result);
                }
                generations.Remove(generation);
            }
        }
    }

    private bool TryGetMemory(string key, out FontNamePreview? preview)
    {
        lock (gate)
        {
            if (!memory.TryGetValue(key, out var entry))
            {
                preview = null;
                return false;
            }
            memoryOrder.Remove(entry.Node);
            memoryOrder.AddLast(entry.Node);
            preview = entry.Preview;
            return true;
        }
    }

    private void Remember(string key, FontNamePreview preview)
    {
        var size = (long)preview.Alpha.Length + MEMORY_ENTRY_OVERHEAD_BYTES;
        if (size > memoryBudgetBytes)
        {
            return;
        }
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            if (memory.Remove(key, out var previous))
            {
                memoryOrder.Remove(previous.Node);
                memoryBytes -= previous.Size;
            }
            while (memoryBytes + size > memoryBudgetBytes && memoryOrder.First is { } oldest)
            {
                memoryBytes -= memory[oldest.Value].Size;
                memory.Remove(oldest.Value);
                memoryOrder.RemoveFirst();
            }
            memory.Add(key, (preview, memoryOrder.AddLast(key), size));
            memoryBytes += size;
        }
    }

    private async Task<FontNamePreview?> ReadDiskAsync(string key, CancellationToken token)
    {
        if (diskBudgetBytes == 0)
        {
            return null;
        }
        await diskGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            InitializeDisk();
            if (!diskAvailable)
            {
                return null;
            }
            var path = CachePath(key);
            if (!File.Exists(path))
            {
                RemoveDiskEntry(key);
                return null;
            }
            var preview = await ReadVerifiedAsync(path, token).ConfigureAwait(false);
            if (preview is null)
            {
                DeleteDiskEntry(key);
                return null;
            }
            TouchDisk(key);
            return preview;
        }
        catch (Exception error) when (IsDiskFailure(error))
        {
            return null;
        }
        finally
        {
            diskGate.Release();
        }
    }

    private async Task WriteDiskAsync(string key, FontNamePreview preview, CancellationToken token)
    {
        if (!HasValidDimensions(preview.PixelWidth, preview.PixelHeight, preview.LogicalWidth, preview.LogicalHeight, preview.Alpha.Length))
        {
            throw new InvalidDataException("Invalid font name preview dimensions.");
        }
        var length = HEADER_BYTES + (long)preview.Alpha.Length + HASH_BYTES;
        if (length > diskBudgetBytes)
        {
            return;
        }
        await diskGate.WaitAsync(token).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            InitializeDisk();
            if (!diskAvailable)
            {
                return;
            }
            temporaryPath = Path.Combine(directoryPath, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
            var bytes = Serialize(preview);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, CachePath(key), true);
            temporaryPath = null;
            RemoveDiskEntry(key);
            disk.Add(key, (length, DateTime.MinValue));
            diskBytes += length;
            TouchDisk(key);
            TrimDisk();
        }
        catch (Exception error) when (IsDiskFailure(error))
        {
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }
            diskGate.Release();
        }
    }

    private async Task TouchDiskAsync(string key, CancellationToken token)
    {
        if (diskBudgetBytes == 0)
        {
            return;
        }
        await diskGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            InitializeDisk();
            if (diskAvailable && disk.ContainsKey(key))
            {
                TouchDisk(key);
            }
        }
        catch (Exception error) when (IsDiskFailure(error))
        {
        }
        finally
        {
            diskGate.Release();
        }
    }

    private void InitializeDisk()
    {
        if (diskInitialized)
        {
            return;
        }
        diskInitialized = true;
        Directory.CreateDirectory(directoryPath);
        foreach (var path in Directory.EnumerateFiles(directoryPath, "*.afnp"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            if (key.Length != 64 || !key.All(char.IsAsciiHexDigit))
            {
                continue;
            }
            var file = new FileInfo(path);
            disk.Add(key, (file.Length, file.LastWriteTimeUtc));
            diskBytes += file.Length;
            latestDiskUse = file.LastWriteTimeUtc > latestDiskUse ? file.LastWriteTimeUtc : latestDiskUse;
        }
        diskAvailable = true;
        TrimDisk();
    }

    private void TouchDisk(string key)
    {
        if (disk.TryGetValue(key, out var entry))
        {
            var now = DateTime.UtcNow;
            latestDiskUse = now > latestDiskUse ? now : latestDiskUse.AddTicks(1);
            disk[key] = (entry.Size, latestDiskUse);
            File.SetLastWriteTimeUtc(CachePath(key), latestDiskUse);
        }
    }

    private void TrimDisk()
    {
        foreach (var key in disk.OrderBy(entry => entry.Value.Used).Select(entry => entry.Key).ToArray())
        {
            if (diskBytes <= diskBudgetBytes)
            {
                break;
            }
            DeleteDiskEntry(key);
        }
    }

    private void DeleteDiskEntry(string key)
    {
        File.Delete(CachePath(key));
        RemoveDiskEntry(key);
    }

    private void RemoveDiskEntry(string key)
    {
        if (disk.Remove(key, out var entry))
        {
            diskBytes -= entry.Size;
        }
    }

    private string CachePath(string key) => Path.Combine(directoryPath, key + ".afnp");

    private static string GetCacheKey(FontNamePreviewRequest request, string fingerprint)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(FORMAT_VERSION);
            writer.Write(fingerprint);
            writer.Write(request.FamilyName);
            writer.Write(request.Variant.HasValue);
            if (request.Variant is { } variant)
            {
                writer.Write(variant.Name);
                writer.Write(variant.PostScriptName is not null);
                if (variant.PostScriptName is { } postScriptName)
                {
                    writer.Write(postScriptName);
                }
                writer.Write(variant.Weight);
                writer.Write(variant.Width);
                writer.Write(variant.Italic);
            }
            writer.Write(request.Text);
            writer.Write(request.FontSize);
            writer.Write(request.RenderScale);
            writer.Write(request.MaxWidth);
        }
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }

    private static byte[] Serialize(FontNamePreview preview)
    {
        using var stream = new MemoryStream(HEADER_BYTES + preview.Alpha.Length + HASH_BYTES);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(MAGIC);
            writer.Write(FORMAT_VERSION);
            writer.Write(preview.PixelWidth);
            writer.Write(preview.PixelHeight);
            writer.Write(preview.LogicalWidth);
            writer.Write(preview.LogicalHeight);
            writer.Write(preview.Alpha.Length);
            writer.Write(preview.Alpha.Span);
        }
        var checksum = SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        stream.Write(checksum);
        return stream.ToArray();
    }

    private static async Task<FontNamePreview?> ReadVerifiedAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length < HEADER_BYTES + HASH_BYTES || stream.Length > HEADER_BYTES + HASH_BYTES + MAX_ALPHA_BYTES)
        {
            return null;
        }
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.Length != bytes.Length)
        {
            return null;
        }
        var payloadLength = bytes.Length - HASH_BYTES;
        var checksum = SHA256.HashData(bytes.AsSpan(0, payloadLength));
        if (!CryptographicOperations.FixedTimeEquals(checksum, bytes.AsSpan(payloadLength)))
        {
            return null;
        }
        using var contents = new MemoryStream(bytes, 0, payloadLength, false);
        using var reader = new BinaryReader(contents, Encoding.UTF8);
        if (reader.ReadUInt32() != MAGIC || reader.ReadInt32() != FORMAT_VERSION)
        {
            return null;
        }
        var pixelWidth = reader.ReadInt32();
        var pixelHeight = reader.ReadInt32();
        var logicalWidth = reader.ReadDouble();
        var logicalHeight = reader.ReadDouble();
        var alphaLength = reader.ReadInt32();
        if (!HasValidDimensions(pixelWidth, pixelHeight, logicalWidth, logicalHeight, alphaLength) ||
            payloadLength != HEADER_BYTES + (long)alphaLength)
        {
            return null;
        }
        return new(pixelWidth, pixelHeight, logicalWidth, logicalHeight, bytes.AsMemory(HEADER_BYTES, alphaLength));
    }

    private static bool HasValidDimensions(int pixelWidth, int pixelHeight, double logicalWidth, double logicalHeight, int length) =>
        pixelWidth is > 0 and <= MAX_PIXEL_DIMENSION && pixelHeight is > 0 and <= MAX_PIXEL_DIMENSION &&
        double.IsFinite(logicalWidth) && logicalWidth > 0 && double.IsFinite(logicalHeight) && logicalHeight > 0 &&
        length is > 0 and <= MAX_ALPHA_BYTES && (long)pixelWidth * pixelHeight == length;

    private static bool IsDiskFailure(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (IsDiskFailure(error))
        {
        }
    }
}
