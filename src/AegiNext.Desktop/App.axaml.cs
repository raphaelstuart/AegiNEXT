using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Diagnostics;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using System.Runtime.Versioning;

namespace AegiNext.Desktop;

/// <summary>
/// 装配桌面应用程序并管理主窗口生命周期。
/// </summary>
public class App : Avalonia.Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        if (!Localization.IsInitialized)
        {
            Localization.Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"));
        }
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS() && NativeMenu.GetMenu(this) is null)
        {
            NativeMenu.SetMenu(this, new NativeMenu());
        }
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var workspaceOptions = WorkspaceProbeOptions.Parse(desktop.Args ?? []);
            var chromeOptions = WindowChromeProbeOptions.Parse(desktop.Args ?? []);
            var probeOptions = HdrProbeOptions.Parse(desktop.Args ?? []);
            if (workspaceOptions.Enabled)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var probe = new WorkspaceProbe(workspaceOptions, desktop);
                desktop.MainWindow = probe.MainWindow;
            }
            else if (chromeOptions.Enabled)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var probe = new WindowChromeProbe(chromeOptions, desktop);
                desktop.MainWindow = probe.MainWindow;
            }
            else if (probeOptions.Enabled)
            {
                if (!OperatingSystem.IsMacOS())
                {
                    throw new PlatformNotSupportedException("当前 HDR 诊断入口仅支持 macOS。");
                }

                ConfigureHdrProbe(desktop, probeOptions);
            }
            else
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = StartDesktopAsync(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartDesktopAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var context = new DesktopApplicationContext();
        try
        {
            await context.Initialization;
            var startup = new DesktopStartupCoordinator(desktop, context);
            startup.Start();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            await context.DisposeAsync();
            desktop.Shutdown(1);
        }
    }

    [SupportedOSPlatform("macos")]
    private static void ConfigureHdrProbe(IClassicDesktopStyleApplicationLifetime desktop, HdrProbeOptions options)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var probe = new HdrProbeWindow(options);
        probe.Closed += (_, _) => desktop.Shutdown(probe.ExitCode);
        desktop.MainWindow = probe;
    }
}
