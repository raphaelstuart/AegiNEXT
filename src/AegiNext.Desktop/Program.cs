using Avalonia;

namespace AegiNext.Desktop;

internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    internal static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
