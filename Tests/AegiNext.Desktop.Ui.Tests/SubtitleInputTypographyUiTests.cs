using AegiNext.Core.Timing;
using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleInputTypographyUiTests
{
    private static readonly string[] mixedText = ["这是什么？123", "这是什么x2", "这是直播吗？", "TEST 123"];

    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task ChineseLatinAndDigitsShareTheActualSubtitleInputFontAndCenteredLineBox(string language)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        for (var index = 0; index < mixedText.Length; index++)
        {
            context.Session.Editor.AddSubtitle(new MediaTime(index * 2), new(index * 2 + 2), mixedText[index]);
        }

        context.Session.SelectCue(context.Session.DocumentSnapshot.Subtitles[0].Id);
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        context.Window.Layouts.Float(WorkbenchPanelIds.SUBTITLES);
        Dispatcher.UIThread.RunJobs();
        var owner = Assert.Single(context.Window.Layouts.FloatingWindows);
        owner.Width = 1100;
        owner.Height = 640;
        owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var list = owner.GetVisualDescendants().OfType<ListBox>().Single(control => control.Name == "SubtitleList");
        var inputs = list.GetVisualDescendants().OfType<TextBox>().Where(input => input.AcceptsReturn).ToArray();
        Assert.Equal(mixedText.Length, inputs.Length);
        Assert.All(inputs, input =>
        {
            Assert.Equal(VerticalAlignment.Center, input.VerticalContentAlignment);
            Assert.Equal(20, input.LineHeight);
            Assert.Equal(inputs[0].FontFamily, input.FontFamily);
            Assert.False(string.IsNullOrWhiteSpace(input.Text));
        });
        var baselines = inputs.Select(input =>
        {
            var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
            var line = Assert.Single(presenter.TextLayout.TextLines);
            Assert.Equal(20, line.Height);
            var baseline = presenter.TranslatePoint(new(0, line.Baseline), input)!.Value.Y;
            Assert.InRange(baseline, 0, input.Bounds.Height);
            return baseline;
        }).ToArray();
        Assert.InRange(baselines.Max() - baselines.Min(), 0, 0.5);
        using var frame = owner.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Assert.True(Path.IsPathFullyQualified(directory));
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"subtitle-input-mixed-{language}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
