using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Analysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsPaletteUiTests
{
    [AvaloniaFact]
    public async Task RestoreDefaultColorsDiscardsInvalidDraftsAndPersistsOnlyColors()
    {
        await using var context = new MainWindowTestContext();
        var original = context.Session.Preferences with
        {
            AccentColor = "#B54880", AudioGraph = AudioGraphPalettes.Get(2), Theme = WorkbenchTheme.DARK,
            Language = "en-US", Volume = 0.4f, WindowMenuOnMac = true
        };
        context.Session.UpdatePreferences(original);
        var document = context.Session.DocumentSnapshot;
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        var accent = UiTestActions.Find<ColorDraftInput>(settings, "AccentPicker");
        var high = UiTestActions.Find<ColorDraftInput>(settings, "AudioHighPicker");
        Type(settings, accent.FindControl<TextBox>("ColorInput")!, "#bad");
        UiTestActions.Press(settings, Key.Enter);
        Type(settings, high.FindControl<TextBox>("ColorInput")!, "#wrong");
        UiTestActions.Press(settings, Key.Enter);
        Assert.True(accent.Draft!.HasError);
        Assert.True(high.Draft!.HasError);
        var changes = new List<SettingsColorsChangedEventArgs>();
        settings.ColorsChanged += (_, value) => changes.Add(value);
        UiTestActions.Click(settings, "ResetColorsButton");
        var defaults = new WorkbenchPreferences();
        var expected = original with { AccentColor = defaults.AccentColor, AudioGraph = defaults.AudioGraph };
        Assert.Equal(expected, context.Session.Preferences);
        Assert.Single(changes);
        Assert.False(accent.Draft.HasError);
        Assert.False(high.Draft.HasError);
        Assert.False(accent.Draft.IsDirty);
        Assert.False(high.Draft.IsDirty);
        Assert.Equal(0, UiTestActions.Find<ComboBox>(settings, "AudioPaletteCombo").SelectedIndex);
        Assert.Same(document, context.Session.DocumentSnapshot);
        using var store = new WorkbenchPreferencesStore(context.Session.PreferencesStore.DirectoryPath);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (store.Load() != expected)
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        settings.Close();
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        Assert.Equal(defaults.AccentColor, settings.ViewModel.Colors.AccentColor);
        Assert.Equal(defaults.AudioGraph, settings.ViewModel.Colors.AudioGraph);
    }

    [AvaloniaFact]
    public async Task ActualPaletteKeyboardSelectionReturnsDirectlyToClassicAcrossRefreshAndReopen()
    {
        await using var context = new MainWindowTestContext();
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        var combo = UiTestActions.Find<ComboBox>(settings, "AudioPaletteCombo");
        foreach (var index in new[] { 1, 0, 2, 0, 3, 0, 1, 2, 3, 0 })
        {
            SelectPalette(settings, combo, index);
            Assert.Equal(index, combo.SelectedIndex);
            Assert.Equal(AudioGraphPalettes.Get(index), context.Session.Preferences.AudioGraph);
            settings.RefreshLanguage();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(index, combo.SelectedIndex);
            Assert.Equal(index, settings.ViewModel.Colors.SchemeIndex);
        }

        SelectPalette(settings, combo, 2);
        settings.Close();
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        combo = UiTestActions.Find<ComboBox>(settings, "AudioPaletteCombo");
        Assert.Equal(2, combo.SelectedIndex);
        SelectPalette(settings, combo, 0);
        Assert.Equal(new AudioGraphPalette(), context.Session.Preferences.AudioGraph);
    }

    [AvaloniaFact]
    public async Task ActualColorsPageAppliesAndPersistsWithoutEditingTheProjectOrDiscardingInvalidColor()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var document = context.Session.DocumentSnapshot;
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        var high = UiTestActions.Find<ColorDraftInput>(settings, "AudioHighPicker");
        var input = high.FindControl<TextBox>("ColorInput")!;
        Type(settings, input, "#bad");
        UiTestActions.Press(settings, Key.Enter);
        Assert.True(high.Draft!.HasError);
        Assert.True(context.Session.Preferences.AudioGraph.UseClassicSpectrum);
        settings.SelectPage(SettingsPage.APPEARANCE);
        UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = 2;
        settings.SelectPage(SettingsPage.COLORS);
        Assert.Equal("#bad", input.Text);
        Assert.True(high.Draft.HasError);
        settings.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(input.Focus());
        UiTestActions.Press(settings, Key.Escape);
        Assert.False(high.Draft.HasError);
        UiTestActions.Find<ComboBox>(settings, "AudioPaletteCombo").SelectedIndex = 1;
        Assert.Equal(AudioGraphPalettes.Get(1), context.Session.Preferences.AudioGraph);
        Assert.Equal(context.Session.Preferences.AudioGraph, UiTestActions.Find<AudioGraphPreviewControl>(settings, "AudioPalettePreview").Palette);
        Type(settings, input, "#F0A020");
        UiTestActions.Press(settings, Key.Enter);
        Assert.Equal("#F0A020", context.Session.Preferences.AudioGraph.High);
        Assert.Equal(AudioGraphPalettes.CUSTOM_INDEX, settings.ViewModel.Colors.SchemeIndex);
        Assert.False(high.Draft.IsDirty);
        Assert.Same(document, context.Session.DocumentSnapshot);
        using var store = new WorkbenchPreferencesStore(context.Session.PreferencesStore.DirectoryPath);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (store.Load().AudioGraph != context.Session.Preferences.AudioGraph)
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Capture(settings, "settings-colors.png");
        settings.Close();
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.COLORS);
        Assert.Equal("#F0A020", UiTestActions.Find<ColorDraftInput>(settings, "AudioHighPicker").Draft!.HexText);
        Assert.Equal(AudioGraphPalettes.CUSTOM_INDEX, UiTestActions.Find<ComboBox>(settings, "AudioPaletteCombo").SelectedIndex);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public void SpectrumPixelsRecolorTheExistingAnalysisAndKeepViewport()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = new SubtitleTimelineControl();
        var window = new Window { Width = 520, Height = 220, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            window.UpdateLayout();
            timeline.SetDocument(new ProjectDocument(), null, null);
            timeline.SetSpectrogram(new SpectrogramData(2, 1, new MediaTime(10), [0, 255], [0, 0, 0, 0]));
            timeline.PixelsPerSecond = (timeline.Bounds.Width - timeline.HeaderWidth) / 10;
            var viewport = timeline.Viewport;
            var original = Pixel(timeline);
            timeline.SetAudioGraphPalette(AudioGraphPalettes.Get(1));
            Assert.Equal(new SKColor(217, 247, 255), Pixel(timeline));
            Assert.NotEqual(original, Pixel(timeline));
            Assert.Equal(viewport, timeline.Viewport);
            timeline.SetAudioGraphPalette(new());
            Assert.Equal(original, Pixel(timeline));
        }
        finally
        {
            window.Close();
        }
    }

    private static SKColor Pixel(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)timeline.Bounds.Width, (int)timeline.Bounds.Height), new(96, 96));
        target.Render(timeline);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var x = (int)(timeline.HeaderWidth + (timeline.Bounds.Width - timeline.HeaderWidth) * 0.75);
        var y = (int)(timeline.RulerHeight + (timeline.Bounds.Height - timeline.RulerHeight) * 0.75);
        return pixels.GetPixel(x, y);
    }

    private static void Type(Window window, TextBox input, string text)
    {
        input.BringIntoView();
        window.UpdateLayout();
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SelectPalette(Window window, ComboBox combo, int index)
    {
        combo.BringIntoView();
        window.UpdateLayout();
        Assert.True(combo.Focus());
        for (var item = combo.SelectedIndex; item > 0; item--)
        {
            UiTestActions.Press(window, Key.Up);
        }
        for (var item = 0; item < index; item++)
        {
            UiTestActions.Press(window, Key.Down);
        }
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
