using System.Buffers.Binary;
using System.Security.Cryptography;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisCacheStore : IDisposable
{
    private const long MAGIC = 0x4145474941554431;
    private const int VERSION = 2;
    private const int MAX_INDEX_BYTES = 64 * 1024 * 1024;
    private readonly Lock gate = new();
    private readonly byte[] identityDigest;
    private readonly AudioAnalysisRecipe recipe;
    private readonly MediaTimelineMapping mapping;
    private readonly MediaTime duration;
    private long maximumCachedBytes;
    private readonly Dictionary<(AudioAnalysisTileKind Kind, int Resolution), List<AudioAnalysisCacheEntry>> entries = [];
    private readonly Dictionary<long, LinkedListNode<KeyValuePair<long, byte[]>>> payloads = [];
    private readonly LinkedList<KeyValuePair<long, byte[]>> lru = new();
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FileStream? data;
    private FileStream? writerLease;
    private string? buildingDirectory;
    private string rootDirectory;
    private Exception? failure;
    private long throughSample;
    private long cachedBytes;
    private long revision;
    private bool complete;
    private bool invalidated;
    private bool disposed;

    internal AudioAnalysisCacheStore(string storeDirectory, string identity, MediaTimelineMapping mapping,
        MediaTime duration, long maximumCachedBytes = 16L * 1024 * 1024, AudioAnalysisRecipe? recipe = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCachedBytes);
        this.recipe = recipe ?? new();
        this.recipe.Validate();
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(identity));
            hash.AppendData(this.recipe.GetDigest());
            identityDigest = hash.GetHashAndReset();
        }
        this.mapping = mapping;
        this.duration = duration;
        this.maximumCachedBytes = maximumCachedBytes;
        rootDirectory = Path.GetFullPath(storeDirectory);
        DirectoryPath = Path.Combine(rootDirectory, Convert.ToHexStringLower(identityDigest));
        throughSample = MediaStart;
        TryOpen();
    }

    internal event EventHandler? Changed;
    internal AudioAnalysisRecipe Recipe => recipe;
    internal Action? ValidateSource { get; set; }
    internal string DirectoryPath { get; private set; }
    internal long MediaStart => mapping.Origin.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
    internal long MediaEnd => mapping.ToMediaTime(duration).ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
    internal long? ConfirmedSourceEnd { get; private set; }
    internal bool IsComplete
    {
        get
        {
            lock (gate)
            {
                return complete;
            }
        }
    }
    internal MediaTime AvailableDuration
    {
        get
        {
            lock (gate)
            {
                return complete ? duration : new(Math.Max(0, throughSample - MediaStart), WaveformAnalyzer.SAMPLE_RATE);
            }
        }
    }
    internal long CachedBytes
    {
        get
        {
            lock (gate)
            {
                return cachedBytes;
            }
        }
    }
    internal long Revision
    {
        get
        {
            lock (gate)
            {
                return revision;
            }
        }
    }

    internal void AppendWaveform(int samplesPerBucket, long firstBucket, ReadOnlySpan<float> peaks)
    {
        if (peaks.Length == 0 || peaks.Length % 2 != 0)
        {
            throw new ArgumentException("波形缓存块必须包含完整峰值对。", nameof(peaks));
        }
        var payload = new byte[checked(peaks.Length * sizeof(float))];
        for (var index = 0; index < peaks.Length; index++)
        {
            if (!float.IsFinite(peaks[index]) || index % 2 == 0 && peaks[index] > peaks[index + 1])
            {
                throw new InvalidDataException("波形缓存包含无效峰值。");
            }
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(index * sizeof(float)), peaks[index]);
        }
        Append(AudioAnalysisTileKind.WAVEFORM, samplesPerBucket, firstBucket, peaks.Length / 2, payload);
    }

    internal void AppendSpectrum(int samplesPerColumn, long firstColumn, int columns, ReadOnlySpan<byte> levels)
    {
        if (columns <= 0 || levels.Length != checked(columns * recipe.FrequencyBins))
        {
            throw new ArgumentException("频谱缓存块尺寸无效。", nameof(levels));
        }
        Append(AudioAnalysisTileKind.SPECTRUM, samplesPerColumn, firstColumn, columns, levels);
    }

    private void Append(AudioAnalysisTileKind kind, int resolution, long firstColumn, int columns, ReadOnlySpan<byte> payload)
    {
        if (resolution <= 0 || (resolution & (resolution - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (complete)
            {
                throw new InvalidOperationException("完整缓存不可追加。");
            }
            EnsureWriter();
            var level = GetLevel(kind, resolution);
            if (level.Count != 0 && level[^1].FirstColumn + level[^1].Columns != firstColumn)
            {
                throw new InvalidDataException("缓存块必须按同一层的连续网格追加。");
            }
            var offset = data!.Length;
            data.Position = offset;
            data.Write(payload);
            level.Add(new(kind, resolution, firstColumn, columns, offset, payload.Length, SHA256.HashData(payload)));
        }
    }

    internal void Publish(long processedThrough, long? confirmedSourceEnd = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            throughSample = Math.Clamp(processedThrough, MediaStart, MediaEnd);
            if (confirmedSourceEnd is { } end)
            {
                ConfirmedSourceEnd = end;
            }
            WakeUnderLock();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Complete(long confirmedSourceEnd)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            EnsureWriter();
            ValidateSource?.Invoke();
            ConfirmedSourceEnd = Math.Clamp(confirmedSourceEnd, MediaStart, MediaEnd);
            throughSample = MediaEnd;
            data!.Flush(true);
            WriteIndex(buildingDirectory!);
            data.Dispose();
            data = null;
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
            Directory.Move(buildingDirectory!, DirectoryPath);
            buildingDirectory = null;
            data = OpenData(DirectoryPath);
            complete = true;
            invalidated = false;
            writerLease?.Dispose();
            writerLease = null;
            WakeUnderLock();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Fail(Exception error)
    {
        lock (gate)
        {
            if (complete)
            {
                return;
            }
            failure = error;
            data?.Dispose();
            data = null;
            writerLease?.Dispose();
            writerLease = null;
            if (buildingDirectory is { } temporary)
            {
                Directory.Delete(temporary, true);
                buildingDirectory = null;
            }
            WakeUnderLock();
        }
    }

    internal Task WaitForUpdateAsync(long observedRevision, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is { } error)
            {
                return Task.FromException(error);
            }
            return observedRevision != revision ? Task.CompletedTask : changed.Task.WaitAsync(cancellationToken);
        }
    }

    internal void Reset()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (complete)
            {
                return;
            }
            failure = null;
            entries.Clear();
            payloads.Clear();
            lru.Clear();
            cachedBytes = 0;
            throughSample = MediaStart;
            ConfirmedSourceEnd = null;
            WakeUnderLock();
        }
    }

    internal void Invalidate()
    {
        lock (gate)
        {
            data?.Dispose();
            data = null;
            complete = false;
            invalidated = true;
            failure = null;
            entries.Clear();
            payloads.Clear();
            lru.Clear();
            cachedBytes = 0;
            throughSample = MediaStart;
            ConfirmedSourceEnd = null;
            WakeUnderLock();
        }
    }

    internal WaveformData? ReadWaveform(WaveformAnalysisRequest request, bool availableOnly)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ThrowIfFailed();
            var start = request.Start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
            var first = start / request.SamplesPerBucket;
            var peaks = new float[request.BucketCount * 2];
            var count = 0;
            AudioAnalysisCacheEntry? previous = null;
            byte[]? payload = null;
            for (; count < request.BucketCount; count++)
            {
                var sample = checked((first + count) * request.SamplesPerBucket);
                if (complete && sample >= Math.Max(0, Math.Min(MediaEnd, ConfirmedSourceEnd ?? MediaEnd) - MediaStart))
                {
                    continue;
                }
                var entry = Find(AudioAnalysisTileKind.WAVEFORM, request.SamplesPerBucket, first + count);
                if (entry is null)
                {
                    if (complete)
                    {
                        throw new InvalidDataException("完整波形缓存缺少索引块。");
                    }
                    if (!availableOnly)
                    {
                        return null;
                    }
                    break;
                }
                var offset = checked((int)(first + count - entry.FirstColumn) * 2 * sizeof(float));
                if (!ReferenceEquals(previous, entry))
                {
                    payload = ReadPayload(entry);
                    previous = entry;
                }
                peaks[count * 2] = BinaryPrimitives.ReadSingleLittleEndian(payload!.AsSpan(offset));
                peaks[count * 2 + 1] = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + sizeof(float)));
            }
            return count == 0 ? null : new(new(request.Start, request.SamplesPerBucket, count, request.Mode), peaks[..(count * 2)]);
        }
    }

    internal SpectrogramData? ReadSpectrum(WaveformAnalysisRequest request, bool availableOnly)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ThrowIfFailed();
            var stride = recipe.HopSize;
            while ((long)stride * 2 <= request.SamplesPerBucket / recipe.Decimation)
            {
                stride = checked(stride * 2);
            }
            var start = mapping.ToMediaTime(request.Start).ToTimestamp(new(1, recipe.SpectrumSampleRate), MediaTimeRounding.FLOOR).Value;
            var end = mapping.ToMediaTime(request.End).ToTimestamp(new(1, recipe.SpectrumSampleRate), MediaTimeRounding.CEILING).Value;
            var first = AudioSpectrumWindowAnalyzer.Floor(start + stride / 2, stride) / stride;
            var after = AudioSpectrumWindowAnalyzer.Floor(end + stride / 2 - 1, stride) / stride + 1;
            var width = checked((int)(after - first));
            var levels = new byte[checked(width * recipe.FrequencyBins)];
            var count = 0;
            AudioAnalysisCacheEntry? previous = null;
            byte[]? payload = null;
            for (; count < width; count++)
            {
                var center = checked((first + count) * stride * recipe.Decimation);
                if (center < MediaStart || complete && center >= Math.Min(MediaEnd, ConfirmedSourceEnd ?? MediaEnd))
                {
                    continue;
                }
                var entry = Find(AudioAnalysisTileKind.SPECTRUM, stride, first + count);
                if (entry is null)
                {
                    if (complete)
                    {
                        throw new InvalidDataException("完整频谱缓存缺少索引块。");
                    }
                    if (!availableOnly)
                    {
                        return null;
                    }
                    break;
                }
                var offset = checked((int)(first + count - entry.FirstColumn));
                if (!ReferenceEquals(previous, entry))
                {
                    payload = ReadPayload(entry);
                    previous = entry;
                }
                for (var row = 0; row < recipe.FrequencyBins; row++)
                {
                    levels[row * width + count] = payload![row * entry.Columns + offset];
                }
            }
            if (count == 0)
            {
                return null;
            }
            if (count != width)
            {
                var cropped = new byte[count * recipe.FrequencyBins];
                for (var row = 0; row < recipe.FrequencyBins; row++)
                {
                    levels.AsSpan(row * width, count).CopyTo(cropped.AsSpan(row * count));
                }
                levels = cropped;
            }
            return new(count, recipe.FrequencyBins,
                mapping.ToProjectTime(new(first * stride - stride / 2, recipe.SpectrumSampleRate)),
                new(stride, recipe.SpectrumSampleRate), levels);
        }
    }

    private AudioAnalysisCacheEntry? Find(AudioAnalysisTileKind kind, int resolution, long column)
    {
        if (!entries.TryGetValue((kind, resolution), out var level))
        {
            return null;
        }
        var low = 0;
        var high = level.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var entry = level[middle];
            if (column < entry.FirstColumn)
            {
                high = middle - 1;
            }
            else if (column >= entry.FirstColumn + entry.Columns)
            {
                low = middle + 1;
            }
            else
            {
                return entry;
            }
        }
        return null;
    }

    private byte[] ReadPayload(AudioAnalysisCacheEntry entry)
    {
        if (payloads.TryGetValue(entry.Offset, out var existing))
        {
            lru.Remove(existing);
            lru.AddFirst(existing);
            return existing.Value.Value;
        }
        var bytes = new byte[entry.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = RandomAccess.Read(data!.SafeFileHandle, bytes.AsSpan(read), entry.Offset + read);
            if (count == 0)
            {
                throw new InvalidDataException("音频缓存数据已截断。");
            }
            read += count;
        }
        if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(entry.Digest))
        {
            throw new InvalidDataException("音频缓存块校验失败。");
        }
        if (bytes.Length <= maximumCachedBytes)
        {
            while (cachedBytes + bytes.Length > maximumCachedBytes && lru.Last is { } oldest)
            {
                cachedBytes -= oldest.Value.Value.Length;
                payloads.Remove(oldest.Value.Key);
                lru.RemoveLast();
            }
            lru.AddFirst(new KeyValuePair<long, byte[]>(entry.Offset, bytes));
            payloads.Add(entry.Offset, lru.First!);
            cachedBytes += bytes.Length;
        }
        return bytes;
    }

    internal void SetMaximumCachedBytes(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        lock (gate)
        {
            maximumCachedBytes = value;
            while (cachedBytes > value && lru.Last is { } oldest)
            {
                cachedBytes -= oldest.Value.Value.Length;
                payloads.Remove(oldest.Value.Key);
                lru.RemoveLast();
            }
        }
    }

    private List<AudioAnalysisCacheEntry> GetLevel(AudioAnalysisTileKind kind, int resolution)
    {
        if (!entries.TryGetValue((kind, resolution), out var level))
        {
            level = [];
            entries.Add((kind, resolution), level);
        }
        return level;
    }

    private void ThrowIfFailed()
    {
        if (failure is { } error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
        }
    }

    private void EnsureWriter()
    {
        if (data is not null)
        {
            return;
        }
        Directory.CreateDirectory(rootDirectory);
        writerLease ??= new(DirectoryPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        buildingDirectory = DirectoryPath + "-" + Guid.NewGuid().ToString("N") + ".building";
        Directory.CreateDirectory(buildingDirectory);
        data = new(Path.Combine(buildingDirectory, "data.bin"), new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.Read,
            BufferSize = 0
        });
    }

    internal async Task<bool> BeginBuildAsync(Func<CancellationToken, Task>? checkpoint, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reused = false;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (complete)
                {
                    return false;
                }
                Directory.CreateDirectory(rootDirectory);
                writerLease ??= TryAcquireWriterLease(DirectoryPath);
                if (writerLease is not null)
                {
                    if (!invalidated)
                    {
                        TryOpen();
                    }
                    if (complete)
                    {
                        writerLease.Dispose();
                        writerLease = null;
                        WakeUnderLock();
                        reused = true;
                    }
                    else
                    {
                        foreach (var abandoned in Directory.EnumerateDirectories(rootDirectory, Path.GetFileName(DirectoryPath) + "-*.building"))
                        {
                            Directory.Delete(abandoned, true);
                        }
                        EnsureWriter();
                        return true;
                    }
                }
            }
            if (reused)
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return false;
            }
            if (checkpoint is not null)
            {
                await checkpoint(cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private void WriteIndex(string directory)
    {
        using var output = new FileStream(Path.Combine(directory, "index.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(CreateIndex());
        output.Flush(true);
    }

    private byte[] CreateIndex()
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
        {
            writer.Write(MAGIC);
            writer.Write(VERSION);
            writer.Write(identityDigest);
            writer.Write(recipe.GetDigest());
            writer.Write(mapping.Origin.Numerator);
            writer.Write(mapping.Origin.Denominator);
            writer.Write(duration.Numerator);
            writer.Write(duration.Denominator);
            writer.Write(ConfirmedSourceEnd!.Value);
            writer.Write(data!.Length);
            writer.Write(entries.Values.Sum(level => level.Count));
            foreach (var entry in entries.Values.SelectMany(level => level).OrderBy(entry => entry.Offset))
            {
                writer.Write((int)entry.Kind);
                writer.Write(entry.Resolution);
                writer.Write(entry.FirstColumn);
                writer.Write(entry.Columns);
                writer.Write(entry.Offset);
                writer.Write(entry.Length);
                writer.Write(entry.Digest);
            }
        }
        var content = memory.ToArray();
        memory.Write(SHA256.HashData(content));
        return memory.ToArray();
    }

    private void TryOpen()
    {
        var indexPath = Path.Combine(DirectoryPath, "index.bin");
        if (!File.Exists(indexPath))
        {
            return;
        }
        try
        {
            using var openingLease = writerLease is null ? TryAcquireWriterLease(DirectoryPath) : null;
            if (writerLease is null && openingLease is null)
            {
                return;
            }
            var length = new FileInfo(indexPath).Length;
            if (length < 160 || length > MAX_INDEX_BYTES)
            {
                throw new InvalidDataException("音频缓存索引尺寸无效。");
            }
            var bytes = File.ReadAllBytes(indexPath);
            if (!SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
            {
                throw new InvalidDataException("音频缓存索引校验失败。");
            }
            using var reader = new BinaryReader(new MemoryStream(bytes, 0, bytes.Length - 32), System.Text.Encoding.UTF8);
            if (reader.ReadInt64() != MAGIC || reader.ReadInt32() != VERSION ||
                !reader.ReadBytes(32).AsSpan().SequenceEqual(identityDigest) ||
                !reader.ReadBytes(32).AsSpan().SequenceEqual(recipe.GetDigest()) ||
                new MediaTime(reader.ReadInt64(), reader.ReadInt64()) != mapping.Origin ||
                new MediaTime(reader.ReadInt64(), reader.ReadInt64()) != duration)
            {
                throw new InvalidDataException("音频缓存身份或版本不匹配。");
            }
            var sourceEnd = reader.ReadInt64();
            var dataLength = reader.ReadInt64();
            var count = reader.ReadInt32();
            if (sourceEnd < MediaStart || sourceEnd > MediaEnd || dataLength < 0 ||
                new FileInfo(Path.Combine(DirectoryPath, "data.bin")).Length != dataLength ||
                count < 0 || count > (bytes.Length - reader.BaseStream.Position) / 64)
            {
                throw new InvalidDataException("音频缓存索引范围无效。");
            }
            long expectedOffset = 0;
            for (var index = 0; index < count; index++)
            {
                var kind = (AudioAnalysisTileKind)reader.ReadInt32();
                var resolution = reader.ReadInt32();
                var first = reader.ReadInt64();
                var columns = reader.ReadInt32();
                var offset = reader.ReadInt64();
                var payloadLength = reader.ReadInt32();
                var digest = reader.ReadBytes(32);
                if (kind is not (AudioAnalysisTileKind.WAVEFORM or AudioAnalysisTileKind.SPECTRUM) ||
                    resolution <= 0 || (resolution & (resolution - 1)) != 0 || columns <= 0 ||
                    payloadLength != checked(columns * (kind == AudioAnalysisTileKind.WAVEFORM ? 8 : recipe.FrequencyBins)) ||
                    offset != expectedOffset || payloadLength > dataLength - offset || digest.Length != 32)
                {
                    throw new InvalidDataException("音频缓存块描述无效。");
                }
                var level = GetLevel(kind, resolution);
                if (level.Count != 0 && level[^1].FirstColumn + level[^1].Columns != first)
                {
                    throw new InvalidDataException("音频缓存层存在空洞或重叠。");
                }
                level.Add(new(kind, resolution, first, columns, offset, payloadLength, digest));
                expectedOffset += payloadLength;
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length || expectedOffset != dataLength)
            {
                throw new InvalidDataException("音频缓存索引尾部无效。");
            }
            data = OpenData(DirectoryPath);
            ConfirmedSourceEnd = sourceEnd;
            throughSample = MediaEnd;
            complete = true;
        }
        catch (Exception error) when (error is IOException or ArgumentException or OverflowException)
        {
            entries.Clear();
            data?.Dispose();
            data = null;
        }
    }

    internal async Task RelocateAsync(string directory, Func<CancellationToken, Task>? checkpoint, CancellationToken cancellationToken)
    {
        string source;
        string destination;
        AudioAnalysisCacheSnapshot captured;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!complete)
            {
                throw new InvalidOperationException("只能迁移完整音频缓存。");
            }
            source = DirectoryPath;
            destination = Path.Combine(Path.GetFullPath(directory), Path.GetFileName(source));
            if (string.Equals(source, destination, StringComparison.Ordinal))
            {
                return;
            }
            captured = new(data!.SafeFileHandle, data.Length, CreateIndex());
        }
        using var snapshot = captured;
        Directory.CreateDirectory(directory);
        using var destinationLease = await AcquireMigrationLeaseAsync(destination, checkpoint, cancellationToken).ConfigureAwait(false);
        var temporary = destination + "-" + Guid.NewGuid().ToString("N") + ".copying";
        Directory.CreateDirectory(temporary);
        try
        {
            await using (var output = new FileStream(Path.Combine(temporary, "data.bin"), FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[1024 * 1024];
                long offset = 0;
                while (offset < snapshot.Length)
                {
                    var count = (int)Math.Min(buffer.Length, snapshot.Length - offset);
                    var read = await RandomAccess.ReadAsync(snapshot.Handle, buffer.AsMemory(0, count), offset, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new InvalidDataException("音频缓存迁移期间数据已截断。");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    offset += read;
                    if (checkpoint is not null)
                    {
                        await checkpoint(cancellationToken).ConfigureAwait(false);
                    }
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }
            await using (var output = new FileStream(Path.Combine(temporary, "index.bin"), FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await output.WriteAsync(snapshot.Index, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }
            Directory.Move(temporary, destination);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (!complete || DirectoryPath != source)
                {
                    throw new InvalidOperationException("音频缓存迁移期间源缓存状态发生变化。");
                }
                var replacement = OpenData(destination);
                data!.Dispose();
                data = replacement;
                DirectoryPath = destination;
                rootDirectory = Path.GetFullPath(directory);
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }

    private static async Task<FileStream> AcquireMigrationLeaseAsync(string destination,
        Func<CancellationToken, Task>? checkpoint, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryAcquireWriterLease(destination) is { } lease)
            {
                return lease;
            }
            if (checkpoint is not null)
            {
                await checkpoint(cancellationToken).ConfigureAwait(false);
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static FileStream? TryAcquireWriterLease(string destination)
    {
        try
        {
            return new(destination + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 ||
                                       OperatingSystem.IsLinux() && (error.HResult & 0xFFFF) == 11 ||
                                       OperatingSystem.IsMacOS() && (error.HResult & 0xFFFF) == 35)
        {
            return null;
        }
    }

    private static FileStream OpenData(string directory)
    {
        return new(Path.Combine(directory, "data.bin"), new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read | FileShare.Delete,
            BufferSize = 0,
            Options = FileOptions.RandomAccess
        });
    }

    private void WakeUnderLock()
    {
        revision++;
        var previous = changed;
        changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            data?.Dispose();
            data = null;
            writerLease?.Dispose();
            writerLease = null;
            if (buildingDirectory is { } temporary)
            {
                Directory.Delete(temporary, true);
            }
            payloads.Clear();
            lru.Clear();
            cachedBytes = 0;
            changed.TrySetCanceled();
        }
    }
}
