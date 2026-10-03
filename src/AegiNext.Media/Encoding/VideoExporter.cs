using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Encoding;

/// <summary>启动邻接的独立导出 worker，管理取消、进度和不覆盖旧文件的原子提交。</summary>
public sealed class VideoExporter
{
    /// <summary>压制当前工程；失败或取消只删除本次临时文件，已有成片保持原样。</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporter is a service instance used by the application boundary.")]
    public async Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        var output = Path.GetFullPath(request.OutputPath);
        if (File.Exists(output))
        {
            throw new IOException("输出文件已存在，请选择新文件名。");
        }

        var directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".aeginext-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath ?? Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"));
            var worker = request.WorkerPath ?? Path.Combine(AppContext.BaseDirectory,
                File.Exists(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "AegiNext.ExportWorker.exe" : "AegiNext.ExportWorker"))
                    ? OperatingSystem.IsWindows() ? "AegiNext.ExportWorker.exe" : "AegiNext.ExportWorker"
                    : "AegiNext.ExportWorker.dll");
            if (!Path.IsPathFullyQualified(worker) || !File.Exists(worker))
            {
                throw new FileNotFoundException("缺少邻接的 AegiNext.ExportWorker，请完整部署应用。", worker);
            }

            var start = new ProcessStartInfo
            {
                FileName = worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ExportExecutable.Resolve("dotnet", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")) : worker,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = temporary
            };
            if (worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add(worker);
            }

            var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(request.Project, ExportWire.Options),
                Path.GetFullPath(request.ProjectDirectory), temporary, Path.GetExtension(output).ToLowerInvariant(),
                request.Codec, request.Preset, request.Crf, request.AudioMode, request.AudioBitrate, ffmpeg);
            var serializedJob = JsonSerializer.Serialize(job, ExportWire.Options);
            using var process = new Process { StartInfo = start };
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动导出 worker。");
            }

            using var cancelled = cancellationToken.Register(() => Kill(process));
            var errors = ReadErrorsAsync(process.StandardError);
            try
            {
                await process.StandardInput.WriteLineAsync(serializedJob.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                ExportWorkerMessage? completed = null;
                string? failure = null;
                while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (line.Length > 65536)
                    {
                        throw new InvalidDataException("导出 worker 消息超过限制。");
                    }

                    var message = JsonSerializer.Deserialize<ExportWorkerMessage>(line, ExportWire.Options)
                        ?? throw new InvalidDataException("导出 worker 返回空消息。");
                    if (message.Type == "progress")
                    {
                        progress?.Report(new(message.Frames, new(message.PositionNumerator, message.PositionDenominator), null, "encoding"));
                    }
                    else if (message.Type == "complete")
                    {
                        completed = message;
                    }
                    else if (message.Type == "error")
                    {
                        failure = message.Error;
                    }
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var diagnostics = await errors.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 || completed is null)
                {
                    throw new InvalidOperationException(failure ?? $"压制失败：{diagnostics}");
                }

                var encoded = Path.Combine(temporary, "output" + job.Extension);
                if (!File.Exists(encoded) || new FileInfo(encoded).Length == 0)
                {
                    throw new InvalidDataException("导出 worker 未产生有效成片。");
                }

                File.Move(encoded, output, false);
                progress?.Report(new(completed.Frames, MediaTime.Zero, 1, "complete"));
                return new(output, completed.Frames);
            }
            finally
            {
                Kill(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await errors.ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }

    internal static void Validate(VideoExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectValidator.Validate(request.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!Path.IsPathFullyQualified(request.ProjectDirectory) || !Path.IsPathFullyQualified(request.OutputPath))
        {
            throw new ArgumentException("工程目录和输出文件必须使用完整路径。", nameof(request));
        }

        if (request.Project.Media is null || request.Project.Width % 2 != 0 || request.Project.Height % 2 != 0)
        {
            throw new ArgumentException("压制需要媒体绑定及偶数视频画布尺寸。", nameof(request));
        }

        if (Path.GetExtension(request.OutputPath).ToLowerInvariant() is not ".mp4" and not ".mkv")
        {
            throw new ArgumentException("首版压制支持 MP4 或 MKV。", nameof(request));
        }

        if (!Enum.IsDefined(request.Codec) || !Enum.IsDefined(request.AudioMode) || request.Crf is < 0 or > 51 ||
            request.Preset is not "ultrafast" and not "superfast" and not "veryfast" and not "faster" and not "fast" and not "medium" and not "slow" and not "slower" and not "veryslow" ||
            request.AudioBitrate is < 32000 or > 512000)
        {
            throw new ArgumentException("不支持的编码参数。", nameof(request));
        }
    }

    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal static async Task<string> ReadErrorsAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            output.Append(buffer, 0, Math.Min(count, Math.Max(0, 65536 - output.Length)));
        }

        return output.ToString();
    }
}
