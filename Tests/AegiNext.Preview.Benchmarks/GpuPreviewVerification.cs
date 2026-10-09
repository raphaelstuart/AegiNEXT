using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Rendering.Projects;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Decoding;
using Avalonia.OpenGL;
using AegiNext.Rendering;
using System.Numerics;

namespace AegiNext.Preview.Benchmarks;

/// <summary>真实硬件上的颜色/alpha/模糊/遮罩回读校验；与纯 CPU 基线逐像素比较。</summary>
internal static class GpuPreviewVerification
{
    internal static void Run(PreviewGraphicsContext graphics,
        IOpenGlTextureSharingRenderInterfaceContextFeature feature, string? mediaPath)
    {
        var results = new List<object>();
        Exception? ownershipError = null;
        var foreignThread = new Thread(() =>
        {
            try { graphics.Dispose(); }
            catch (Exception error) { ownershipError = error; }
        });
        foreignThread.Start();
        foreignThread.Join();
        if (ownershipError is not InvalidOperationException)
        {
            throw new InvalidDataException("A foreign thread disposed the GPU context.", ownershipError);
        }
        results.Add(new { Name = "context-owner-thread", Passed = true });
        using var cpu = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var gpu = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), graphicsContext: graphics.Context);
        var rectangle = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 16, 16),
            Transform = new(X: 8, Y: 8), Fill = new(4, 2, 0.25, 0.5)
        };
        var document = new ProjectDocument { Width = 32, Height = 32, Layers = [rectangle] };
        using (var shaper = new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"))))
        using (var text = shaper.Shape("HDR", 24, TextDirection.LEFT_TO_RIGHT, "en"))
        using (var cpuText = new LinearRenderSurface(new(128, 48, 203)))
        using (var gpuText = new LinearRenderSurface(new(128, 48, 203), graphics.Context))
        {
            cpuText.DrawText(text, new Vector2(8, 32), new(4, 2, 0.25f, 0.5f));
            gpuText.DrawText(text, new Vector2(8, 32), new(4, 2, 0.25f, 0.5f));
            var expected = new Half[cpuText.Info.ChannelCount];
            var actual = new Half[gpuText.Info.ChannelCount];
            cpuText.CopyPixels(expected);
            gpuText.CopyPixels(actual);
            if (expected.Zip(actual, (first, second) => Math.Abs((double)first - (double)second)).Max() > 0.02)
            {
                throw new InvalidDataException("GPU public surface clamped HDR text.");
            }
            results.Add(new { Name = "surface-HDR-text", Passed = true });
        }
        Compare("extended-premultiplied", document);
        Compare("clip-opacity", document with
        {
            Layers = [rectangle with { Opacity = 0.5 }]
        });
        Compare("blur", document with { Layers = [rectangle with { Blur = 2 }] });
        var foreground = new ProjectTrack();
        foreach (var blend in Enum.GetValues<BlendMode>())
        {
            Compare("blend-" + blend, document with
            {
                Tracks = [foreground, ProjectTrack.Default],
                Layers = [rectangle with { Fill = new(2, 0.5, 0.25) }, rectangle with { Id = Guid.NewGuid(), TrackId = foreground.Id, Blend = blend, Fill = new(3, 0.25, 0.5) }]
            });
        }

        foreach (var white in new[] { 100d, 203d, 406d })
        {
            var preview = new ProjectDocument { Width = 2, Height = 1, ReferenceWhiteNits = white,
                Layers = [rectangle with { Transform = new(), Shape = new(ShapeKind.RECTANGLE, 1, 1), Fill = new(0.25, 0.25, 0.25) }] };
            byte[] background = [128, 128, 128, 255, 64, 96, 128, 255];
            var expected = cpu.ComposePreview(preview, MediaTime.Zero, background, 2, 1, 8);
            var actual = gpu.ComposePreview(preview, MediaTime.Zero, background, 2, 1, 8);
            CheckByteDifference("reference-white-" + white, expected, actual, 1);
            if (!actual.AsSpan(4).SequenceEqual(background.AsSpan(4)))
            {
                throw new InvalidDataException("GPU composition changed the video background.");
            }
        }

        var subtitle = new SubtitleLine { Text = "MMMMMMMM", End = new(2), Style = new()
        {
            FontFamily = "Arial", FontSize = 32, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
            Fill = new(4, 0.5, 0.25), StrokeWidth = 2, ShadowOffset = new(4, 4), ShadowBlur = 2
        } };
        var masked = new ProjectDocument { Width = 128, Height = 96, Subtitles = [subtitle], Layers = [new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End, Blur = 1,
            Mask = new RectangleClipMask { TopLeft = new(32, 0), BottomRight = new(80, 96) }
        }] };
        Compare("subtitle-plain", masked with { Subtitles = [subtitle with { Style = subtitle.Style with { StrokeWidth = 0, ShadowColor = SceneColor.Transparent } }], Layers = [masked.Layers[0] with { Mask = null, Blur = 0 }] });
        Compare("subtitle-stroke-shadow", masked with { Layers = [masked.Layers[0] with { Mask = null, Blur = 0 }] });
        Compare("subtitle-blur", masked with { Layers = [masked.Layers[0] with { Mask = null }] });
        var maskPixels = Pixels(gpu, masked);
        var retained = 0;
        for (var y = 0; y < 96; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                var offset = (y * 128 + x) * 4;
                if (x < 32 || x >= 80)
                {
                    if (maskPixels.AsSpan(offset, 4).ContainsAnyExcept((Half)0))
                    {
                        throw new InvalidDataException("GPU subtitle shadow/blur escaped the clip mask.");
                    }
                }
                else if (maskPixels[offset + 3] > (Half)0)
                {
                    retained++;
                }
            }
        }
        if (retained == 0)
        {
            throw new InvalidDataException("GPU subtitle disappeared inside its mask.");
        }
        Compare("subtitle-mask-blur", masked);
        var cachedPixels = new float[masked.Width * masked.Height * 4];
        var cacheUpdate = gpu.UpdateCachedFramePixels(masked, new(1), cachedPixels, 0);
        var uncachedPixels = Pixels(gpu, masked).Select(value => (float)value).ToArray();
        if (!cacheUpdate.Updated || cacheUpdate.Empty || !cachedPixels.SequenceEqual(uncachedPixels))
        {
            throw new InvalidDataException("GPU F32 foreground differs from its uncached F16 raster.");
        }
        cachedPixels.AsSpan().Fill(float.NaN);
        var cacheHit = gpu.UpdateCachedFramePixels(masked, new(1), cachedPixels, cacheUpdate.Revision);
        if (cacheHit.Updated || cacheHit.Revision != cacheUpdate.Revision || cachedPixels.Any(value => !float.IsNaN(value)))
        {
            throw new InvalidDataException("GPU foreground cache copied a retained revision.");
        }
        var beforeReuse = gpu.RenderSurfaceStatistics.Reuses;
        using (gpu.Render(masked, new(1)))
        using (gpu.Render(masked, new(1)))
        {
        }
        var pooled = gpu.RenderSurfaceStatistics;
        if (pooled.Reuses < beforeReuse + 2 || pooled.ActiveLeases != 0 || pooled.RetainedBytes > pooled.MaximumRetainedBytes)
        {
            throw new InvalidDataException("GPU temporary surfaces were not reused within the retained budget.");
        }
        results.Add(new { Name = "GPU-F32-revision-and-surface-reuse", Passed = true, pooled.Allocations, pooled.Reuses });
        var black = Enumerable.Range(0, 128 * 96).SelectMany(_ => new byte[] { 0, 0, 0, 255 }).ToArray();
        var composed = gpu.ComposePreview(masked, new(1), black, 128, 96, 128 * 4, 64, 48);
        var same = gpu.ComposePreview(masked, new(1), black, 128, 96, 128 * 4, 64, 48);
        if (!composed.SequenceEqual(same))
        {
            throw new InvalidDataException("GPU cached scene changed at the same time.");
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            gpu.ComposePreview(masked, new(1), black, 128, 96, 512, 64, 48, cancellation.Token);
            throw new InvalidDataException("GPU composition ignored cancellation.");
        }
        catch (OperationCanceledException)
        {
            results.Add(new { Name = "cancellation", Passed = true });
        }
        if (mediaPath is not null)
        {
            using var decoder = FfmpegVideoDecoder.Open(Path.GetFullPath(mediaPath), 0);
            using var frame = decoder.ReadFrame() ?? throw new InvalidDataException("No fixture frame.");
            var state = new ProjectPreviewState(masked, AppContext.BaseDirectory);
            var catalog = new PreviewFrameCatalog();
            Exception? renderError = null;
            using var cpuConverter = new ProjectPreviewConverter(() => state);
            using var gpuConverter = new ProjectPreviewConverter(() => state, error => renderError = error,
                previewFrames: catalog, getGraphics: () => feature);
            foreach (var interactive in new[] { false, true, false })
            {
                state = state with { IsInteractive = interactive, TargetTime = new(1), Quality = PreviewQuality.HIGH,
                    QualityRevision = state.QualityRevision + 1 };
                var expected = cpuConverter.Convert(frame);
                var actual = gpuConverter.Convert(frame);
                if (!gpuConverter.UsesGpu || renderError is not null ||
                    catalog.FindIdentity(actual) is not { } identity || identity.Interactive != interactive ||
                    identity.QualityRevision != state.QualityRevision || !ReferenceEquals(identity.Document, masked))
                {
                    throw new InvalidDataException("GPU converter did not preserve the delivered frame identity.", renderError);
                }
                // 缩放后的纹理插值和硬件 sRGB 量化允许少量末位差异，平均误差必须低于一个灰阶的 1/5。
                CheckByteDifference("converter-interactive-" + interactive, expected.Pixels.ToArray(), actual.Pixels.ToArray(), 5, 0.2);
            }
            GpuPlaybackVerification.Run(mediaPath, masked, feature).GetAwaiter().GetResult();
            results.Add(new { Name = "playback-worker-seek-play-close", Passed = true });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Graphics = graphics.Description, Checks = results }));

        void Compare(string name, ProjectDocument project, double meanLimit = 0.002, double maximumLimit = 0.02)
        {
            var expected = Pixels(cpu, project);
            var actual = Pixels(gpu, project);
            var errors = expected.Zip(actual, (first, second) => Math.Abs((double)first - (double)second)).ToArray();
            var mean = errors.Average();
            var maximum = errors.Max();
            if (!double.IsFinite(mean) || mean > meanLimit || maximum > maximumLimit)
            {
                throw new InvalidDataException($"{name}: mean error {mean}, maximum {maximum}.");
            }
            results.Add(new { Name = name, MeanError = mean, MaximumError = maximum });
        }

        void CheckByteDifference(string name, byte[] expected, byte[] actual, int limit, double meanLimit = 1)
        {
            var errors = expected.Zip(actual, (first, second) => Math.Abs(first - second)).ToArray();
            var maximum = errors.Max();
            var mean = errors.Average();
            if (maximum > limit || mean > meanLimit)
            {
                throw new InvalidDataException($"{name}: BGRA error {maximum}.");
            }
            results.Add(new { Name = name, MaximumError = maximum, MeanError = mean });
        }
    }

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        using var surface = renderer.Render(document, new(1));
        var result = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(result);
        return result;
    }
}
