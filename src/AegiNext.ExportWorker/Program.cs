using System.Text;
using System.Text.Json;
using AegiNext.Application;
using AegiNext.Media.Encoding;

namespace AegiNext.ExportWorker;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            var input = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (input is null || input.Length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("导出 worker 需要有限大小的快照请求。");
            }

            var job = JsonSerializer.Deserialize<ExportWorkerJob>(input, ExportWire.Options)
                ?? throw new InvalidDataException("缺少导出请求。");
            var document = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(job.Project.GetRawText()));
            var output = Path.Combine(job.TemporaryDirectory, "output" + job.Extension);
            var request = new VideoExportRequest(document, job.ProjectDirectory, output)
            {
                Codec = job.Codec, Preset = job.Preset, Crf = job.Crf, AudioMode = job.AudioMode,
                AudioBitrate = job.AudioBitrate, FfmpegPath = job.FfmpegPath,
                EncodingMode = job.EncodingMode, VideoBitrate = job.VideoBitrate
            };
            var progress = new WorkerProgress();
            var encoded = NativeVideoExport.Run(request, Path.Combine(job.TemporaryDirectory, "video.nut"), progress, CancellationToken.None);
            await ExportMuxer.RunAsync(request, Path.Combine(job.TemporaryDirectory, "video.nut"), CancellationToken.None).ConfigureAwait(false);
            Send(new("complete", encoded.Frames, Encoder: encoded.Encoder));
            return 0;
        }
        catch (Exception error)
        {
            Send(new("error", Error: error.Message));
            await Console.Error.WriteLineAsync(error.ToString()).ConfigureAwait(false);
            return 1;
        }
    }

    internal static void Send(ExportWorkerMessage message)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(message, ExportWire.Options));
        Console.Out.Flush();
    }
}
