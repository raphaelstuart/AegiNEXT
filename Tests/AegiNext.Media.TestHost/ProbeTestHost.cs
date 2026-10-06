using System.Text.Json;

namespace AegiNext.Media.TestHost;

/// <summary>
/// 为媒体适配器测试提供确定性的跨平台子进程。
/// </summary>
public static class ProbeTestHost
{
    private static async Task<int> Main(string[] args)
    {
        switch (args[0])
        {
            case "-v":
                return await VideoTimingProbeTestHost.RunAsync(args).ConfigureAwait(false);
            case "arguments":
                Console.Write(JsonSerializer.Serialize(args[1..]));
                return 0;
            case "exit":
                Console.Write("{\"streams\":[]}");
                Console.Error.Write("fixture failure");
                return 7;
            case "pipes":
                for (var index = 0; index < 100; index++)
                {
                    await Console.Out.WriteAsync(new string('o', 4096)).ConfigureAwait(false);
                    await Console.Error.WriteAsync(new string('e', 4096)).ConfigureAwait(false);
                }

                return 0;
            case "overflow":
                var output = args[1] == "stderr" ? Console.Error : Console.Out;
                while (true)
                {
                    await output.WriteAsync(new string('x', 4096)).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                }
            case "wait":
                await File.WriteAllTextAsync(args[1] + ".writing", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
                File.Move(args[1] + ".writing", args[1]);
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
                return 0;
            default:
                return 64;
        }
    }
}
