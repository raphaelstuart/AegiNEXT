using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Media.TestHost;

internal static class VideoTimingProbeTestHost
{
    private const string IDENTITY = """
        {"program_version":{"version":"9.0.2"},"library_versions":[
         {"name":"libavutil","major":61,"minor":1,"micro":102,"version":3998054},
         {"name":"libavcodec","major":63,"minor":1,"micro":102,"version":4129126},
         {"name":"libavformat","major":63,"minor":1,"micro":102,"version":4129126},
         {"name":"libswscale","major":10,"minor":1,"micro":102,"version":655718},
         {"name":"libswresample","major":7,"minor":1,"micro":102,"version":459110}]}
        """;

    internal static async Task<int> RunAsync(string[] arguments)
    {
        var inputIndex = Array.IndexOf(arguments, "-i");
        if (inputIndex < 0)
        {
            Console.Write(IDENTITY);
            return 0;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(arguments[inputIndex + 1]).ConfigureAwait(false));
        var root = document.RootElement;
        var mode = root.GetProperty("mode").GetString();
        if (mode == "wait")
        {
            var pidFile = root.GetProperty("pidFile").GetString()!;
            await File.WriteAllTextAsync(pidFile + ".writing", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            File.Move(pidFile + ".writing", pidFile);
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        if (mode == "overflow")
        {
            while (true)
            {
                await Console.Out.WriteAsync(new string('x', 4096)).ConfigureAwait(false);
                await Console.Out.FlushAsync().ConfigureAwait(false);
            }
        }

        var result = JsonNode.Parse(IDENTITY)!;
        if (mode == "version")
        {
            result["program_version"]!["version"] = "9.0.3";
        }

        if (mode == "decode-error")
        {
            Console.Error.Write("fixture decoding error after partial frame output");
        }

        if (mode == "change-source")
        {
            var source = arguments[inputIndex + 1];
            var previousWrite = File.GetLastWriteTimeUtc(source);
            await File.WriteAllTextAsync(source, "modified media source during frame scan").ConfigureAwait(false);
            File.SetLastWriteTimeUtc(source, previousWrite.AddSeconds(1));
        }

        if (mode == "replace-source")
        {
            var source = arguments[inputIndex + 1];
            var previousWrite = File.GetLastWriteTimeUtc(source);
            var content = await File.ReadAllTextAsync(source).ConfigureAwait(false);
            await File.WriteAllTextAsync(source + ".replacement", content).ConfigureAwait(false);
            File.SetLastWriteTimeUtc(source + ".replacement", previousWrite);
            File.SetCreationTimeUtc(source + ".replacement", File.GetCreationTimeUtc(source).AddSeconds(1));
            File.Move(source + ".replacement", source, overwrite: true);
        }

        result["streams"] = JsonNode.Parse("""[{"index":2,"codec_type":"video","time_base":"1/1000"}]""");
        result["frames"] = JsonNode.Parse("""[{"stream_index":2,"key_frame":1,"pts":5000,"best_effort_timestamp":5000},{"stream_index":2,"key_frame":0,"pts":5100,"best_effort_timestamp":5100}]""");
        await File.WriteAllTextAsync(arguments[inputIndex + 1] + ".arguments", JsonSerializer.Serialize(arguments)).ConfigureAwait(false);
        Console.Write(result.ToJsonString());
        return 0;
    }
}
