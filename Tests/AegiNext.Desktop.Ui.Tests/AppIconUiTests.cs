using System.Buffers.Binary;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证桌面图标的嵌入资源、平台尺寸和窗口呈现行为。</summary>
public sealed class AppIconUiTests
{
    private const int ICO_HEADER_LENGTH = 6;
    private const int ICO_ENTRY_LENGTH = 16;
    private static readonly int[] windowsIconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    private static readonly Uri iconUri = new("avares://aegi-next/Assets/AppIcon.ico");

    /// <summary>独立解码 ICO 中每个尺寸，覆盖任务栏和高 DPI 的系统图标。</summary>
    [AvaloniaFact]
    public void EmbeddedWindowsIconContainsDecodableFramesForEveryTargetSize()
    {
        var bytes = ReadResource(iconUri);
        Assert.True(bytes.Length >= ICO_HEADER_LENGTH);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        Assert.Equal(windowsIconSizes.Length, count);
        var directoryLength = ICO_HEADER_LENGTH + count * ICO_ENTRY_LENGTH;
        Assert.True(bytes.Length >= directoryLength);
        var actualSizes = new List<int>();

        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan(ICO_HEADER_LENGTH + index * ICO_ENTRY_LENGTH, ICO_ENTRY_LENGTH);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);
            Assert.Equal((byte)0, entry[3]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            Assert.InRange(offset, (uint)directoryLength, (uint)bytes.Length);
            Assert.InRange(length, 1u, (uint)bytes.Length - offset);

            var singleFrame = new byte[ICO_HEADER_LENGTH + ICO_ENTRY_LENGTH + checked((int)length)];
            bytes.AsSpan(0, ICO_HEADER_LENGTH).CopyTo(singleFrame);
            BinaryPrimitives.WriteUInt16LittleEndian(singleFrame.AsSpan(4), 1);
            entry.CopyTo(singleFrame.AsSpan(ICO_HEADER_LENGTH));
            BinaryPrimitives.WriteUInt32LittleEndian(singleFrame.AsSpan(ICO_HEADER_LENGTH + 12),
                ICO_HEADER_LENGTH + ICO_ENTRY_LENGTH);
            bytes.AsSpan(checked((int)offset), checked((int)length))
                .CopyTo(singleFrame.AsSpan(ICO_HEADER_LENGTH + ICO_ENTRY_LENGTH));
            using var stream = new MemoryStream(singleFrame);
            using var bitmap = new Bitmap(stream);
            Assert.Equal(new PixelSize(width, height), bitmap.PixelSize);
            actualSizes.Add(width);
        }

        Assert.Equal(windowsIconSizes, actualSizes.Order().ToArray());
    }

    /// <summary>确认 Avalonia 默认回退图标和显式窗口图标引用同一份完整资源。</summary>
    [AvaloniaFact]
    public void AvaloniaDefaultIconMatchesTheExplicitApplicationIcon()
    {
        var defaultIcon = ReadResource(new("avares://aegi-next/!__AvaloniaDefaultWindowIcon"));
        var explicitIcon = ReadResource(iconUri);
        Assert.Equal(explicitIcon, defaultIcon);
        using var stream = new MemoryStream(defaultIcon);
        using var bitmap = new Bitmap(stream);
        Assert.True(bitmap.PixelSize.Width > 0);
        Assert.Equal(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
    }

    /// <summary>真实主窗、Dock 浮窗、设置窗及普通窗口均使用应用级图标。</summary>
    [AvaloniaFact]
    public async Task MainFloatingSettingsAndOrdinaryWindowsReceiveTheApplicationIcon()
    {
        await using var context = new MainWindowTestContext();
        var ordinary = new Window { Width = 320, Height = 240 };
        var settings = new SettingsWindow(new());
        Window? floating = null;
        try
        {
            ordinary.Show(context.Window);
            settings.Show(context.Window);
            context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
            Dispatcher.UIThread.RunJobs();
            floating = Assert.Single(context.Window.Layouts.FloatingWindows);
            Assert.IsType<WorkbenchFloatingHostWindow>(floating);

            foreach (var window in new[] { context.Window, floating, settings, ordinary })
            {
                FlushLayout(window);
                Assert.True(window.IsVisible);
                Assert.IsType<WindowIcon>(window.Icon);
                foreach (var titleBar in window.GetVisualDescendants().OfType<WindowTitleBar>())
                {
                    Assert.DoesNotContain(titleBar.GetVisualDescendants().OfType<Image>(),
                        image => image.Name == "ApplicationIcon");
                }
            }
        }
        finally
        {
            floating?.Close();
            settings.Close();
            ordinary.Close();
            Assert.False(settings.IsVisible);
            Assert.False(ordinary.IsVisible);
            Assert.True(floating is null || !floating.IsVisible);
        }
    }

    private static byte[] ReadResource(Uri uri)
    {
        Assert.True(AssetLoader.Exists(uri));
        using var source = AssetLoader.Open(uri);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void FlushLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
