using System.Threading.Channels;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;
using Avalonia.OpenGL;

namespace AegiNext.Preview.Benchmarks;

/// <summary>经由真实准备/交付循环验证专用 GPU 线程与关闭时的资源回收。</summary>
internal static class GpuPlaybackVerification
{
    internal static async Task Run(string mediaPath, ProjectDocument document,
        IOpenGlTextureSharingRenderInterfaceContextFeature feature)
    {
        var state = new ProjectPreviewState(document, AppContext.BaseDirectory);
        var catalog = new PreviewFrameCatalog();
        var frames = Channel.CreateUnbounded<VideoPreviewUpdate>();
        ProjectPreviewConverter? converter = null;
        Exception? renderError = null;
        var initialConverterCount = AegiNext.Media.Preview.SdrVideoConverter.LiveConverterCount;
        var initialFrameCount = FfmpegVideoDecoder.GetLiveFrameCount();
        await using (var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(8))),
            (path, index, clock, options) => new VideoPlaybackSession(
                token => VideoFrameNavigator.Open(path, index, options, token), externalPosition: clock),
            () => converter = new ProjectPreviewConverter(() => Volatile.Read(ref state),
                error => Volatile.Write(ref renderError, error), catalog, getGraphics: () => feature),
            (action, token) => { token.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; },
            update => { if (update.Frame is not null) { frames.Writer.TryWrite(update); } }))
        {
            controller.ConfigureDecodeMode(VideoDecodeMode.Hardware);
            await controller.OpenAsync(Path.GetFullPath(mediaPath));
            Check(await ReceiveFrame());
            var target = new MediaTime(1);
            Volatile.Write(ref state, state with { TargetTime = target });
            await controller.SeekAsync(target);
            VideoPreviewUpdate sought;
            do { sought = await ReceiveFrame(); } while (sought.Snapshot.Position != target);
            Check(sought);
            if (!sought.Snapshot.IsPresentedFrameCurrent)
            {
                throw new InvalidDataException("GPU seek did not deliver a frame covering the requested position.");
            }
            await controller.PlayAsync();
            for (var index = 0; index < 3; index++) { Check(await ReceiveFrame()); }
            await controller.PauseAsync();
            if (controller.Snapshot.Error is not null || Volatile.Read(ref renderError) is not null)
            {
                throw new InvalidDataException("GPU playback pipeline failed.", controller.Snapshot.Error ?? renderError);
            }
        }
        if (AegiNext.Media.Preview.SdrVideoConverter.LiveConverterCount != initialConverterCount ||
            FfmpegVideoDecoder.GetLiveFrameCount() != initialFrameCount)
        {
            throw new InvalidDataException("GPU playback shutdown retained native frames or converters.");
        }

        async Task<VideoPreviewUpdate> ReceiveFrame() =>
            await frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        void Check(VideoPreviewUpdate update)
        {
            if (converter?.UsesGpu != true || Volatile.Read(ref renderError) is not null ||
                update.Snapshot.Error is not null || catalog.FindIdentity(update.Frame!)?.Document != document)
            {
                throw new InvalidDataException("GPU playback lost composition or frame identity.", update.Snapshot.Error ?? renderError);
            }
        }
    }
}
