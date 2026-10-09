using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Startup;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class FontNamePreviewUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualFontNamesRenderInBothThemesAndCacheToApplicationData(bool dark)
    {
        using var environment = new UiTestEnvironment();
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        await application.Initialization;
        await application.Fonts.EnsureLoadedAsync();
        var families = application.Fonts.Candidates.Where(candidate => candidate.Selection.Variant is not null)
            .GroupBy(candidate => candidate.Selection.FamilyName).Where(family => family.Count() > 1).ToArray();
        var familyCandidates = (families.FirstOrDefault(family => family.Key == "Noto Sans SC") ?? families.First()).ToArray();
        var picker = new FontFamilyPicker { Width = 300, PreviewProvider = application.Fonts.PreviewProvider };
        picker.RefreshFontCandidates(familyCandidates);
        picker.SetCurrentFont(familyCandidates.First(candidate => candidate.Selection.Variant?.Weight >= 700).Selection);
        var window = CreateWindow(picker);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            var presenter = Assert.Single(family.GetVisualDescendants().OfType<FontNamePreviewPresenter>());
            await WaitForPreviewAsync(window, () => presenter.HasPreview);
            Assert.NotNull(presenter.Foreground);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Right);
            Flush(window);
            var variantPresenters = family.Items.Cast<MenuItem>().SelectMany(item =>
                item.GetVisualDescendants().OfType<FontNamePreviewPresenter>()).ToArray();
            Assert.NotEmpty(variantPresenters);
            await WaitForPreviewAsync(window, () => variantPresenters.All(item => item.HasPreview));
            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(environment.DirectoryPath, "caches", "fonts", "v1"), "*.afnp"));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (Environment.GetEnvironmentVariable("AEGINEXT_FONT_PREVIEW_REVIEW_DIRECTORY") is { Length: > 0 } reviewDirectory)
            {
                Directory.CreateDirectory(reviewDirectory);
                frame.Save(Path.Combine(reviewDirectory, dark ? "font-menu-dark.png" : "font-menu-light.png"), PngBitmapEncoderOptions.Default);
            }
            Assert.True(picker.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MenuNamesRequestTheirActualFontAndVariantWithoutChangingTheSelection()
    {
        var provider = new FontNamePreviewTestProvider();
        var picker = CreatePicker(provider);
        var current = picker.CurrentFont;
        var commits = 0;
        picker.FamilyCommitted += (_, _) => commits++;
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var familyRequest = Assert.Single(provider.Requests);
            Assert.Equal("Fixture", familyRequest.Text);
            Assert.Equal(current.FamilyName, familyRequest.FamilyName);
            Assert.Equal(current.Variant, familyRequest.Variant);
            var family = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            Assert.Equal("Fixture", family.Header);
            Assert.True(Assert.Single(family.GetVisualDescendants().OfType<FontNamePreviewPresenter>()).HasPreview);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Right);
            Flush(window);
            Assert.True(family.IsSubMenuOpen);
            foreach (var variant in family.Items.Cast<MenuItem>())
            {
                var candidate = Assert.IsType<FontPickerCandidate>(variant.Tag);
                var request = Assert.Single(provider.Requests, request => request.Text == candidate.Selection.Variant?.Name);
                Assert.Equal(candidate.Selection.Variant, request.Variant);
                Assert.Equal(candidate.Selection.FamilyName, request.FamilyName);
                Assert.Equal(candidate.Selection.Variant?.Name, variant.Header);
                Assert.True(Assert.Single(variant.GetVisualDescendants().OfType<FontNamePreviewPresenter>()).HasPreview);
            }
            Assert.Equal(current, picker.CurrentFont);
            Assert.Equal(current.DisplayName, picker.Text);
            Assert.Equal(0, commits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OnlyVisibleMenuNamesAreRenderedAndScrollingRequestsNewNames()
    {
        var provider = new FontNamePreviewTestProvider();
        var picker = CreatePicker(provider);
        picker.RefreshFontCandidates(Enumerable.Range(0, 50).Select(index =>
            new FontPickerCandidate(new($"Family {index:D2}", new SubtitleFontVariant { Name = "Regular" }, true))));
        picker.SetCurrentFont(new("Family 00", new SubtitleFontVariant { Name = "Regular" }, true));
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            Assert.NotEmpty(provider.Requests);
            Assert.True(provider.Requests.Count < 50);
            Assert.DoesNotContain(provider.Requests, request => request.Text == "Family 49");
            var last = GetMenu(picker).Items.Cast<MenuItem>().Last();
            last.BringIntoView();
            Flush(window);
            Assert.Contains(provider.Requests, request => request.Text == "Family 49");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task APreviewThatCompletesAfterTheMenuClosesCannotPresent()
    {
        var provider = new FontNamePreviewTestProvider { Deferred = true };
        var picker = CreatePicker(provider);
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            var presenter = Assert.Single(family.GetVisualDescendants().OfType<FontNamePreviewPresenter>());
            Assert.Single(provider.Requests);
            picker.IsDropDownOpen = false;
            Flush(window);
            Assert.True(Assert.Single(provider.Tokens).IsCancellationRequested);
            provider.Completion.SetResult(provider.Result);
            await Task.Delay(20);
            Flush(window);
            Assert.False(presenter.HasPreview);
            Assert.Equal("Fixture Black", picker.Text);
        }
        finally
        {
            window.Close();
            provider.Completion.TrySetResult(null);
        }
    }

    [AvaloniaFact]
    public async Task ReplacingTheProviderRejectsAnOlderPreviewAndKeepsTheMenuIdentity()
    {
        var previous = new FontNamePreviewTestProvider { Deferred = true };
        var picker = CreatePicker(previous);
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var oldFamily = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            var oldPresenter = Assert.Single(oldFamily.GetVisualDescendants().OfType<FontNamePreviewPresenter>());
            var replacement = new FontNamePreviewTestProvider();
            picker.PreviewProvider = replacement;
            Flush(window);
            var family = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            var presenter = Assert.Single(family.GetVisualDescendants().OfType<FontNamePreviewPresenter>());
            Assert.True(presenter.HasPreview);
            Assert.True(Assert.Single(previous.Tokens).IsCancellationRequested);
            previous.Completion.SetResult(previous.Result);
            await Task.Delay(20);
            Flush(window);
            Assert.False(oldPresenter.HasPreview);
            Assert.True(presenter.HasPreview);
            Assert.Equal("Fixture", family.Header);
            Assert.Equal("Fixture Black", picker.CurrentFont.DisplayName);
        }
        finally
        {
            window.Close();
            previous.Completion.TrySetResult(null);
        }
    }

    [AvaloniaFact]
    public void ThemeForegroundChangesDoNotGenerateASecondRaster()
    {
        var provider = new FontNamePreviewTestProvider();
        var picker = CreatePicker(provider);
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            var presenter = Assert.Single(family.GetVisualDescendants().OfType<FontNamePreviewPresenter>());
            Assert.True(presenter.HasPreview);
            presenter.Foreground = Brushes.White;
            Flush(window);
            Assert.Single(provider.Requests);
            Assert.True(presenter.HasPreview);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UnavailablePreviewKeepsTheReadableNameAndCanStillCommitTheVariant()
    {
        var provider = new FontNamePreviewTestProvider { Result = null };
        var picker = CreatePicker(provider);
        var window = CreateWindow(picker);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetMenu(picker).Items.Cast<MenuItem>());
            var presenter = Assert.Single(family.GetVisualDescendants().OfType<FontNamePreviewPresenter>());
            Assert.Equal("Fixture", presenter.Text);
            Assert.False(presenter.HasPreview);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Right);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            Assert.Equal("Fixture Regular", picker.CurrentFont.DisplayName);
        }
        finally
        {
            window.Close();
        }
    }

    private static FontFamilyPicker CreatePicker(IFontNamePreviewProvider provider)
    {
        var picker = new FontFamilyPicker { Width = 300, PreviewProvider = provider };
        FontPickerCandidate[] candidates =
        [
            new(new("Fixture", new SubtitleFontVariant { Name = "Regular", Weight = 400, PostScriptName = "Fixture-Regular" }, true)),
            new(new("Fixture", new SubtitleFontVariant { Name = "Black", Weight = 900, PostScriptName = "Fixture-Black" }, true))
        ];
        picker.RefreshFontCandidates(candidates);
        picker.SetCurrentFont(candidates[1].Selection);
        return picker;
    }

    private static Window CreateWindow(FontFamilyPicker picker) => new()
    {
        Width = 500, Height = 400, Content = new StackPanel { Children = { picker, new Button() } }
    };

    private static MenuBase GetMenu(FontFamilyPicker picker) =>
        Assert.IsAssignableFrom<MenuBase>(Assert.Single(picker.GetVisualDescendants().OfType<Popup>()).Child);

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task WaitForPreviewAsync(Window window, Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            Flush(window);
        }
        Assert.True(ready(), "The actual font name preview did not complete.");
    }
}
