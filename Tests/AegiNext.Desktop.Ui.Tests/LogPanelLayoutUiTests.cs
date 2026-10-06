using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using Path = Avalonia.Controls.Shapes.Path;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LogPanelLayoutUiTests
{
    [AvaloniaTheory]
    [InlineData(false, 640)]
    [InlineData(false, 1100)]
    [InlineData(true, 640)]
    [InlineData(true, 1100)]
    public void CollapsedRowsAlignAndInlineDetailsUseTheWindowBackground(bool dark, int width)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage("zh-CN");
        using var journal = new WorkbenchLogJournal();
        using var model = new LogPanelViewModel(journal);
        using var view = new LogPanelView(model, journal);
        var window = new Window
        {
            Width = width,
            Height = 540,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            journal.Append(WorkbenchLogLevel.INFO, "Project", "已打开工程 TEST 123");
            journal.Append(WorkbenchLogLevel.INFO, "Project", "已打开工程 TEST 123", "工程：测试工程.aegiproj\n视频：VideoToolbox 1920 × 1080");
            var longMessage = string.Join(" ", Enumerable.Repeat("混合文字 Video decoder 123", 8));
            journal.Append(WorkbenchLogLevel.INFO, "Decoder", longMessage);
            journal.Append(WorkbenchLogLevel.INFO, "Decoder", longMessage, "详细诊断：" + longMessage);
            window.Show();
            Flush(window);
            var list = view.FindControl<ListBox>("LogEntries")!;
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.Equal(4, rows.Length);
            var plain = Summary(rows[0], model.Entries[0]);
            var detailed = Summary(rows[1], model.Entries[1]);
            Assert.Equal(rows[0].Bounds.Height, rows[1].Bounds.Height, 3);
            Assert.Equal(plain.Bounds.Height, detailed.Bounds.Height, 3);
            Assert.Equal(plain.TranslatePoint(default, rows[0]), detailed.TranslatePoint(default, rows[1]));
            Assert.Equal(plain.FontFamily, detailed.FontFamily);
            Assert.Equal(plain.LineHeight, detailed.LineHeight);
            Assert.True(Summary(rows[2], model.Entries[2]).TextLayout.TextLines.Count > 1);
            Assert.True(Summary(rows[3], model.Entries[3]).TextLayout.TextLines.Count > 1);
            Assert.All(rows, row => Assert.InRange(Summary(row, (WorkbenchLogEntry)row.DataContext!).Bounds.Right, 0, row.Bounds.Width));

            var expander = Assert.Single(rows[1].GetVisualDescendants().OfType<Expander>(), control => control.IsEffectivelyVisible);
            var header = Assert.Single(expander.GetVisualDescendants().OfType<ToggleButton>());
            var triangle = Assert.Single(header.GetVisualDescendants().OfType<Path>());
            AssertTriangleAtTextEnd(detailed, triangle);
            var wrappedSummary = Summary(rows[3], model.Entries[3]);
            var wrappedTriangle = Assert.Single(rows[3].GetVisualDescendants().OfType<Path>(), control => control.IsEffectivelyVisible);
            AssertTriangleAtTextEnd(wrappedSummary, wrappedTriangle);
            Assert.DoesNotContain(rows[0].GetVisualDescendants().OfType<Path>(), control => control.IsEffectivelyVisible);
            AssertBackground(window, list, header);
            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            Flush(window);
            AssertBackground(window, list, header);
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Flush(window);
            Capture(window, $"log-{(dark ? "dark" : "light")}-{width}-collapsed.png");

            var collapsedHeight = rows[1].Bounds.Height;
            var headerPosition = detailed.TranslatePoint(default, rows[1]);
            Assert.True(triangle.Data!.Bounds.Height > triangle.Data.Bounds.Width);
            Click(window, triangle);
            Assert.True(expander.IsExpanded);
            Assert.True(rows[1].Bounds.Height > collapsedHeight);
            Assert.Equal(headerPosition, detailed.TranslatePoint(default, rows[1]));
            Assert.True(triangle.Data!.Bounds.Width > triangle.Data.Bounds.Height);
            var details = Assert.Single(expander.GetVisualDescendants().OfType<SelectableTextBlock>());
            Assert.Equal(model.Entries[1].Details, details.Text);
            Assert.True(details.IsEffectivelyVisible);
            Assert.Equal(detailed.Foreground, triangle.Fill);
            window.MouseMove(new(window.Width - 2, window.Height - 2));
            Flush(window);
            AssertBackground(window, list, header);
            Capture(window, $"log-{(dark ? "dark" : "light")}-{width}-expanded.png");

            Assert.True(header.Focus());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(window);
            Assert.False(expander.IsExpanded);
            Assert.Equal(collapsedHeight, rows[1].Bounds.Height, 3);
            Assert.False(details.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBlock Summary(Control row, WorkbenchLogEntry entry)
    {
        return Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible &&
            (text.Text == entry.Summary || text.Inlines?.OfType<Run>().Any(run => run.Text == entry.Summary) == true));
    }

    private static void AssertTriangleAtTextEnd(TextBlock text, Path triangle)
    {
        var position = triangle.TranslatePoint(default, text)!.Value;
        var lines = text.TextLayout.TextLines;
        Assert.InRange(Math.Abs(position.X + triangle.Bounds.Width - lines[^1].WidthIncludingTrailingWhitespace), 0, 0.5);
        var lastLineCenter = lines.Take(lines.Count - 1).Sum(line => line.Height) + lines[^1].Height / 2;
        Assert.InRange(Math.Abs(position.Y + triangle.Bounds.Height / 2 - lastLineCenter), 0, 1);
    }

    private static void AssertBackground(Window window, ListBox list, Control header)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background);
        var expected = new SKColor(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A);
        var emptyPoint = list.TranslatePoint(new(4, list.Bounds.Height - 4), window)!.Value;
        var headerPoint = header.TranslatePoint(new(header.Bounds.Width - 4, header.Bounds.Height / 2), window)!.Value;
        Assert.Equal(expected, pixels.GetPixel((int)emptyPoint.X, (int)emptyPoint.Y));
        Assert.Equal(expected, pixels.GetPixel((int)headerPoint.X, (int)headerPoint.Y));
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(System.IO.Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static void Click(Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
