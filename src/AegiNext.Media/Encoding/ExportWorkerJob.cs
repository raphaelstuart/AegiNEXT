using System.Text.Json;

namespace AegiNext.Media.Encoding;

internal sealed record ExportWorkerJob(JsonElement Project, string ProjectDirectory, string TemporaryDirectory,
    string Extension, VideoCodec Codec, string Preset, int Crf, AudioExportMode AudioMode, int AudioBitrate, string FfmpegPath);
