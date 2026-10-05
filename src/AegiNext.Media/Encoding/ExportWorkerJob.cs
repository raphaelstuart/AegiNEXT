using System.Text.Json;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Encoding;

internal sealed record ExportWorkerJob(JsonElement Project, string ProjectDirectory, string TemporaryDirectory,
    string Extension, VideoCodec Codec, string Preset, int Crf, AudioExportMode AudioMode, int AudioBitrate, string FfmpegPath,
    VideoEncodingMode EncodingMode = VideoEncodingMode.SOFTWARE, int VideoBitrate = 8000000,
    VideoDecodeMode DecodeMode = VideoDecodeMode.Auto);
