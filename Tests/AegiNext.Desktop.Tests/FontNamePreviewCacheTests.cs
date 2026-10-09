using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

/// <summary>验证字体名称缓存的真实磁盘复用、并发与生命周期边界。</summary>
public sealed class FontNamePreviewCacheTests
{
    /// <summary>缓存跨应用实例复用蒙版，命中时只解析字体身份。</summary>
    [Fact]
    public async Task ASecondInstanceLoadsTheVerifiedDiskPreviewWithoutRendering()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        FontNamePreview first;
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            first = Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None));
        }
        Assert.Single(Directory.GetFiles(directory.Path, "*.afnp"));
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            var second = Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None));
            Assert.Equal(first.Alpha.ToArray(), second.Alpha.ToArray());
            Assert.Equal(first.LogicalWidth, second.LogicalWidth);
            Assert.Equal(first.LogicalHeight, second.LogicalHeight);
        }
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(2, renderer.ResolveCount);
        Assert.Equal(2, renderer.DisposedFaceCount);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>同请求并发只运行一次解析和生成。</summary>
    [Fact]
    public async Task ConcurrentCallersShareOneGeneration()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new FontNamePreviewTestRenderer
        {
            RenderHandler = (request, token) =>
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5), token));
                return Preview(request);
            }
        };
        await using var cache = new FontNamePreviewCache(directory.Path, renderer);
        var first = cache.GetPreviewAsync(Request(), CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var others = Enumerable.Range(0, 12).Select(_ => cache.GetPreviewAsync(Request(), CancellationToken.None).AsTask()).ToArray();
        release.Set();
        var results = await Task.WhenAll(others.Append(first)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Equal(1, renderer.ResolveCount);
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(1, renderer.DisposedFaceCount);
    }

    /// <summary>实际字体身份、文字、变种、尺寸和 DPI 变化均使旧缓存失效。</summary>
    [Fact]
    public async Task AChangedFingerprintOrRequestDoesNotReuseTheOldPreview()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        await using var cache = new FontNamePreviewCache(directory.Path, renderer);
        var request = Request();
        await cache.GetPreviewAsync(request, CancellationToken.None);
        await cache.GetPreviewAsync(request, CancellationToken.None);
        Assert.Equal(1, renderer.RenderCount);
        renderer.Fingerprint = "font-name-alpha8-v2:fixture-font-b";
        await cache.GetPreviewAsync(request, CancellationToken.None);
        await cache.GetPreviewAsync(request with { Text = "Bold" }, CancellationToken.None);
        await cache.GetPreviewAsync(request with { FontSize = 16 }, CancellationToken.None);
        await cache.GetPreviewAsync(request with { RenderScale = 2 }, CancellationToken.None);
        await cache.GetPreviewAsync(request with { MaxWidth = 120 }, CancellationToken.None);
        await cache.GetPreviewAsync(request with { Variant = request.Variant!.Value with { Weight = 700 } }, CancellationToken.None);
        Assert.Equal(7, renderer.RenderCount);
    }

    /// <summary>非法头部、截断和蒙版内容损坏均重新生成。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CorruptDiskEntriesAreReplacedWithAValidPreview(int corruption)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            await cache.GetPreviewAsync(Request(), CancellationToken.None);
        }
        var path = Assert.Single(Directory.GetFiles(directory.Path, "*.afnp"));
        var bytes = await File.ReadAllBytesAsync(path);
        if (corruption == 0)
        {
            bytes[0] ^= 0x80;
        }
        else if (corruption == 1)
        {
            bytes = bytes[..^1];
        }
        else
        {
            bytes[^1] ^= 0x40;
        }
        await File.WriteAllBytesAsync(path, bytes);
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            Assert.Equal(Preview(Request()).Alpha.ToArray(),
                Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None)).Alpha.ToArray());
        }
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            await cache.GetPreviewAsync(Request(), CancellationToken.None);
        }
        Assert.Equal(2, renderer.RenderCount);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>取消一个菜单请求不取消其他调用方共享的生成。</summary>
    [Fact]
    public async Task CancellingOneCallerDoesNotCancelSharedGeneration()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new FontNamePreviewTestRenderer
        {
            RenderHandler = (request, token) =>
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5), token));
                return Preview(request);
            }
        };
        await using var cache = new FontNamePreviewCache(directory.Path, renderer);
        var first = cache.GetPreviewAsync(Request(), cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = cache.GetPreviewAsync(Request(), CancellationToken.None).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        release.Set();
        Assert.IsType<FontNamePreview>(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, renderer.RenderCount);
    }

    /// <summary>关闭服务取消正在运行及等待的生成，释放字体句柄并等待后台清理。</summary>
    [Fact]
    public async Task DisposingCancelsRunningAndQueuedRequestsAndReleasesTheirFaces()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new FontNamePreviewTestRenderer
        {
            RenderHandler = (request, token) =>
            {
                entered.TrySetResult();
                release.Wait(token);
                return Preview(request);
            }
        };
        var cache = new FontNamePreviewCache(directory.Path, renderer, maxConcurrentGenerations: 1);
        var running = cache.GetPreviewAsync(Request(), CancellationToken.None).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = cache.GetPreviewAsync(Request() with { Text = "Other" }, CancellationToken.None).AsTask();
        await cache.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(1, renderer.DisposedFaceCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cache.GetPreviewAsync(Request(), CancellationToken.None).AsTask());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>不可写的磁盘路径仍提供可复用的内存蒙版。</summary>
    [Fact]
    public async Task AnUnwritableCacheDirectoryStillReturnsAndReusesTheMemoryPreview()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var blockedPath = Path.Combine(directory.Path, "occupied-file");
        await File.WriteAllTextAsync(blockedPath, "occupied");
        var renderer = new FontNamePreviewTestRenderer();
        await using var cache = new FontNamePreviewCache(blockedPath, renderer);
        var first = await cache.GetPreviewAsync(Request(), CancellationToken.None);
        var second = await cache.GetPreviewAsync(Request(), CancellationToken.None);
        Assert.IsType<FontNamePreview>(first);
        Assert.Same(first, second);
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal("occupied", await File.ReadAllTextAsync(blockedPath));
    }

    /// <summary>磁盘容量受限时淘汰最近最少使用项，命中会更新使用顺序。</summary>
    [Fact]
    public async Task TheDiskBudgetEvictsTheLeastRecentlyUsedPreview()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        await using (var probe = new FontNamePreviewCache(directory.Path, renderer))
        {
            await probe.GetPreviewAsync(Request(), CancellationToken.None);
        }
        var entryBytes = new FileInfo(Assert.Single(Directory.GetFiles(directory.Path, "*.afnp"))).Length;
        var second = Request() with { Text = "Second" };
        var third = Request() with { Text = "Third" };
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer, memoryBudgetBytes: 0, diskBudgetBytes: entryBytes * 2))
        {
            await cache.GetPreviewAsync(second, CancellationToken.None);
            await cache.GetPreviewAsync(Request(), CancellationToken.None);
            await cache.GetPreviewAsync(third, CancellationToken.None);
            Assert.Equal(2, Directory.GetFiles(directory.Path, "*.afnp").Length);
            Assert.True(Directory.GetFiles(directory.Path, "*.afnp").Sum(path => new FileInfo(path).Length) <= entryBytes * 2);
            var renders = renderer.RenderCount;
            await cache.GetPreviewAsync(Request(), CancellationToken.None);
            Assert.Equal(renders, renderer.RenderCount);
            await cache.GetPreviewAsync(second, CancellationToken.None);
            Assert.Equal(renders + 1, renderer.RenderCount);
        }
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>关闭磁盘缓存且内存容量不足时不会无限保存蒙版。</summary>
    [Fact]
    public async Task APreviewLargerThanTheMemoryAndDiskBudgetsIsNotRetained()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        await using var cache = new FontNamePreviewCache(directory.Path, renderer, memoryBudgetBytes: 1, diskBudgetBytes: 1);
        Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None));
        Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None));
        Assert.Equal(2, renderer.RenderCount);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.afnp"));
    }

    /// <summary>内存预算会淘汰最近最少使用项，内存命中更新使用顺序。</summary>
    [Fact]
    public async Task TheMemoryBudgetEvictsTheLeastRecentlyUsedPreview()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        await using var cache = new FontNamePreviewCache(directory.Path, renderer, memoryBudgetBytes: 1024, diskBudgetBytes: 0);
        var second = Request() with { Text = "Second" };
        await cache.GetPreviewAsync(Request(), CancellationToken.None);
        await cache.GetPreviewAsync(second, CancellationToken.None);
        await cache.GetPreviewAsync(Request() with { Text = "Third" }, CancellationToken.None);
        await cache.GetPreviewAsync(Request(), CancellationToken.None);
        await cache.GetPreviewAsync(Request() with { Text = "Fourth" }, CancellationToken.None);
        Assert.Equal(4, renderer.RenderCount);
        await cache.GetPreviewAsync(Request(), CancellationToken.None);
        Assert.Equal(4, renderer.RenderCount);
        await cache.GetPreviewAsync(second, CancellationToken.None);
        Assert.Equal(5, renderer.RenderCount);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    /// <summary>即使校验码有效，非法图像尺寸及长度也不得进入内存。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidDimensionsAreRejectedEvenWhenTheChecksumMatches(int corruption)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer();
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            await cache.GetPreviewAsync(Request(), CancellationToken.None);
        }
        var path = Assert.Single(Directory.GetFiles(directory.Path, "*.afnp"));
        var bytes = await File.ReadAllBytesAsync(path);
        if (corruption == 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), int.MaxValue);
        }
        else if (corruption == 1)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), 7);
        }
        else
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16), double.NaN);
        }
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
        await File.WriteAllBytesAsync(path, bytes);
        await using (var cache = new FontNamePreviewCache(directory.Path, renderer))
        {
            Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None));
        }
        Assert.Equal(2, renderer.RenderCount);
    }

    /// <summary>生成失败完成所有共享请求并释放字体，下次调用可以重试。</summary>
    [Fact]
    public async Task AFailedGenerationReleasesItsFaceAndAllowsRetry()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var renderer = new FontNamePreviewTestRenderer
        {
            RenderHandler = (_, _) => throw new InvalidOperationException("Fixture render failure")
        };
        await using var cache = new FontNamePreviewCache(directory.Path, renderer);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetPreviewAsync(Request(), CancellationToken.None).AsTask());
        Assert.Equal(1, renderer.DisposedFaceCount);
        renderer.RenderHandler = PreviewWithCancellation;
        Assert.IsType<FontNamePreview>(await cache.GetPreviewAsync(Request(), CancellationToken.None));
        Assert.Equal(2, renderer.RenderCount);
        Assert.Equal(2, renderer.DisposedFaceCount);
    }

    /// <summary>不同字体请求的昂贵生成只占用配置数量的后台任务。</summary>
    [Fact]
    public async Task DifferentRequestsRespectTheGenerationConcurrencyLimit()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var countsGate = new Lock();
        var active = 0;
        var maximum = 0;
        var renderer = new FontNamePreviewTestRenderer
        {
            RenderHandler = (request, token) =>
            {
                lock (countsGate)
                {
                    ++active;
                    maximum = Math.Max(maximum, active);
                }
                if (entered.CurrentCount > 0)
                {
                    entered.Signal();
                }
                try
                {
                    Assert.True(release.Wait(TimeSpan.FromSeconds(5), token));
                    return Preview(request);
                }
                finally
                {
                    lock (countsGate)
                    {
                        --active;
                    }
                }
            }
        };
        await using var cache = new FontNamePreviewCache(directory.Path, renderer);
        var tasks = Enumerable.Range(0, 4)
            .Select(index => cache.GetPreviewAsync(Request() with { Text = index.ToString(CultureInfo.InvariantCulture) }, CancellationToken.None).AsTask()).ToArray();
        Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
        Assert.Equal(2, renderer.RenderCount);
        release.Set();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, maximum);
        Assert.Equal(4, renderer.DisposedFaceCount);
    }

    private static FontNamePreviewRequest Request() => new("Fixture", new SubtitleFontVariant
    {
        Name = "Regular", PostScriptName = "Fixture-Regular"
    }, "Fixture", 14, 1, 280);

    private static FontNamePreview Preview(FontNamePreviewRequest request) =>
        new(4, 2, 4 / request.RenderScale, 2 / request.RenderScale, new byte[] { 0, 10, 100, 255, 255, 100, 10, 0 });

    private static FontNamePreview PreviewWithCancellation(FontNamePreviewRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Preview(request);
    }
}
