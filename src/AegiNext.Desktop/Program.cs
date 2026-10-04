using Avalonia;

namespace AegiNext.Desktop;

internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        if (args is ["--package-media-probe", var media, var report])
        {
            return Diagnostics.PackageMediaProbe.RunAsync(media, report).GetAwaiter().GetResult();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    internal static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
