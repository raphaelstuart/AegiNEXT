using AegiNext.Core.Projects;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Encoding;

/// <summary>当前工程的独立压制快照；资源目录必须显式指定，最终输出不会覆盖现有文件。</summary>
public sealed record VideoExportRequest(ProjectDocument Project, string ProjectDirectory, string OutputPath)
{
    public VideoCodec Codec { get; init; } = VideoCodec.Auto;
    public VideoEncodingMode EncodingMode { get; init; } = VideoEncodingMode.SOFTWARE;
    public VideoDecodeMode DecodeMode { get; init; } = VideoDecodeMode.Auto;
    public int VideoBitrate { get; init; } = 8000000;
    public string Preset { get; init; } = "medium";
    public int Crf { get; init; } = 20;
    public AudioExportMode AudioMode { get; init; } = AudioExportMode.Copy;
    public int AudioBitrate { get; init; } = 192000;
    public string? FfmpegPath { get; init; }
    public string? WorkerPath { get; init; }
}
