namespace AegiNext.Preview.Benchmarks;

internal sealed record PreviewBenchmarkSample(int Round, int Index, string TargetTime, string VideoTime, int Width, int Height,
    double DecodeMilliseconds, double ConvertMilliseconds, double ComposeMilliseconds, double TotalMilliseconds,
    double CodecMilliseconds, double DownloadMilliseconds);
