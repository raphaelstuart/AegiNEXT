using System.Globalization;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleFontVariantHostUiTests
{
    public static bool HasMacNotoFonts
    {
        get
        {
            if (!OperatingSystem.IsMacOS())
            {
                return false;
            }
            using var styles = SKFontManager.Default.GetFontStyles("Noto Sans SC");
            return styles.Count > 0;
        }
    }

    [AvaloniaFact(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    public async Task StylesPanelKeepsBlackDuringBackfillAndUsesOneUndoForEachSelection()
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        await context.OpenMediaAsync();
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "字体 ABC 123");
        context.Session.SelectCue(cueId);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(cueId);
        var picker = UiTestActions.Find<FontFamilyPicker>(context.Window, "FontCombo");
        var bold = UiTestActions.Find<ToolbarToggleButton>(context.Window, "BoldCheck");
        var semiBold = Variant(picker.FontCandidates, 600);
        var black = Variant(picker.FontCandidates, 900);
        picker.Text = semiBold.DisplayName;
        Flush(context.Window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.True(picker.CommitText());
        Flush(context.Window);
        var semiBoldSnapshot = context.Session.DocumentSnapshot;
        var style = Assert.Single(semiBoldSnapshot.Subtitles).Style;
        Assert.Equal(semiBold.Variant, style.FontVariant);
        Assert.False(style.Bold);
        Assert.False(bold.IsChecked);
        Assert.Equal(semiBold.DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Flush(context.Window);
        Assert.Equal(semiBold.Variant, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style.FontVariant);

        context.Session.Editor.Reset(semiBoldSnapshot);
        context.Session.SelectCue(cueId);
        picker.Text = black.DisplayName;
        Flush(context.Window);
        Assert.True(picker.CommitText());
        Flush(context.Window);
        var blackSnapshot = context.Session.DocumentSnapshot;
        style = Assert.Single(blackSnapshot.Subtitles).Style;
        Assert.Equal(black.Variant, style.FontVariant);
        Assert.True(style.Bold);
        Assert.True(bold.IsChecked);
        Assert.Equal(black.DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Same(semiBoldSnapshot, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Flush(context.Window);
        Assert.Equal(black.Variant, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style.FontVariant);
        Assert.Equal(black.DisplayName, picker.Text);

        UiTestActions.Click(context.Window, "BoldCheck");
        Flush(context.Window);
        var regularSnapshot = context.Session.DocumentSnapshot;
        style = Assert.Single(regularSnapshot.Subtitles).Style;
        Assert.Equal(400, style.FontVariant?.Weight);
        Assert.False(style.Bold);
        Assert.False(bold.IsChecked);
        Assert.Equal(Variant(picker.FontCandidates, 400).DisplayName, picker.Text);
        UiTestActions.Click(context.Window, "BoldCheck");
        Flush(context.Window);
        style = Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style;
        Assert.Equal(700, style.FontVariant?.Weight);
        Assert.True(style.Bold);
        Assert.Equal(Variant(picker.FontCandidates, 700).DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Same(regularSnapshot, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());
        Flush(context.Window);
        Assert.Equal(700, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style.FontVariant?.Weight);
    }

    [AvaloniaFact(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    public async Task DetailsVariantCommitChangesOnlyTheSelectedCharacterAndCreatesOneTransaction()
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        var semiBold = Variant(context.Session.Fonts.Candidates, 600);
        var black = Variant(context.Session.Fonts.Candidates, 900);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { FontFamily = semiBold.FamilyName, FontVariant = semiBold.Variant, Bold = false }
        });
        context.Session.SelectCue(cueId);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(cueId);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 1);
        Flush(host);
        var picker = UiTestActions.Find<FontFamilyPicker>(host, "SelectionFontInput");
        var bold = UiTestActions.Find<ToggleButton>(host, "BoldSelectionButton");
        Assert.Equal(semiBold.DisplayName, picker.Text);
        Assert.False(bold.IsChecked);
        picker.Text = black.DisplayName;
        Flush(host);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Details.StyleDraft.IsDirty);
        Assert.True(picker.CommitText());
        Flush(host);
        var blackSnapshot = context.Session.DocumentSnapshot;
        var edited = Assert.Single(blackSnapshot.Subtitles);
        Assert.Equal(original.Subtitles[0].Style, edited.Style);
        var span = Assert.Single(edited.InlineSpans);
        Assert.Equal(0, span.Utf16Start);
        Assert.Equal(1, span.Utf16Length);
        Assert.Equal(black.Variant, span.Style.FontVariant);
        Assert.True(span.Style.ApplyTo(edited.Style).Bold);
        Assert.Equal(600, edited.Style.FontVariant?.Weight);
        Assert.True(bold.IsChecked);
        Assert.Equal(black.DisplayName, picker.Text);
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(semiBold.DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Redo());
        Flush(host);
        Assert.Equal(black.Variant, Assert.Single(Assert.Single(context.Session.DocumentSnapshot.Subtitles).InlineSpans).Style.FontVariant);
        Assert.Equal(black.DisplayName, picker.Text);
        rich.SetSelection(0, 1);
        UiTestActions.Click(host, "BoldSelectionButton");
        Flush(host);
        var regularSnapshot = context.Session.DocumentSnapshot;
        edited = Assert.Single(regularSnapshot.Subtitles);
        var regular = Assert.Single(edited.InlineSpans).Style.ApplyTo(edited.Style);
        Assert.Equal(400, regular.FontVariant?.Weight);
        Assert.False(regular.Bold);
        Assert.False(bold.IsChecked);
        Assert.Equal(600, edited.Style.FontVariant?.Weight);
        Assert.Equal(Variant(picker.FontCandidates, 400).DisplayName, picker.Text);
        UiTestActions.Click(host, "BoldSelectionButton");
        Flush(host);
        edited = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        var selected = Assert.Single(edited.InlineSpans).Style.ApplyTo(edited.Style);
        Assert.Equal(700, selected.FontVariant?.Weight);
        Assert.True(selected.Bold);
        Assert.Equal(600, edited.Style.FontVariant?.Weight);
        Assert.Equal(Variant(picker.FontCandidates, 700).DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        Assert.Same(regularSnapshot, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());
        Flush(host);
        edited = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(700, Assert.Single(edited.InlineSpans).Style.FontVariant?.Weight);
    }

    [AvaloniaFact(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    public async Task NamedTrackPresetAndTimingCreationRetainTheSameSystemVariant()
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        var black = Variant(context.Session.Fonts.Candidates, 900);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Noto Black track", new()
        {
            FontFamily = black.FamilyName,
            FontVariant = black.Variant,
            Bold = true
        });
        await context.Session.Styles.UpsertAsync(preset);
        var trackId = context.Session.Editor.AddTrack("Named variant track");
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        await context.Session.ApplySubtitleTrackStyleAsync(trackId, preset.Id);
        var styled = context.Session.DocumentSnapshot;
        var track = styled.Tracks.Single(value => value.Id == trackId);
        Assert.Equal(black.Variant, track.DefaultStyle?.FontVariant);
        Assert.True(track.DefaultStyle?.Bold);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Equal(black.Variant, context.Session.DocumentSnapshot.Tracks.Single(value => value.Id == trackId).DefaultStyle?.FontVariant);
        Assert.True(context.Session.SelectTrack(trackId));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.F8);
        await context.Session.WaitForProjectIdleAsync();
        Flush(context.Window);
        var cue = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(trackId, context.Session.ClipIndex.GetSubtitleTrackId(cue.Id));
        Assert.Equal(black.Variant, cue.Style.FontVariant);
        Assert.Equal(preset.Style, cue.Style);
        Assert.True(context.Session.Editor.Undo());
        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(black.Variant, context.Session.DocumentSnapshot.Tracks.Single(value => value.Id == trackId).DefaultStyle?.FontVariant);
    }

    [AvaloniaFact(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    public async Task RawBlackDraftCommitsOnHostLostFocusWithItsActualBoldStateAndOneUndo()
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        var regular = Variant(context.Session.Fonts.Candidates, 400);
        var black = Variant(context.Session.Fonts.Candidates, 900);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Raw font draft 字体");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { FontFamily = regular.FamilyName, FontVariant = regular.Variant, Bold = false }
        });
        context.Session.SelectCue(cueId);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(cueId);
        var picker = UiTestActions.Find<FontFamilyPicker>(context.Window, "FontCombo");
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        var commits = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => commits.Add(value.Selection);
        input.BringIntoView();
        Flush(context.Window);
        Assert.True(input.Focus());
        input.SelectAll();
        context.Window.KeyTextInput(black.DisplayName);
        Flush(context.Window);
        Assert.Equal(black.DisplayName, picker.Text);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Empty(commits);
        Assert.True(UiTestActions.Find<Button>(context.Window, "ManageStylesButton").Focus());
        Flush(context.Window);
        var edited = Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style;
        Assert.Equal(black.Variant, edited.FontVariant);
        Assert.True(edited.Bold);
        Assert.True(UiTestActions.Find<ToolbarToggleButton>(context.Window, "BoldCheck").IsChecked);
        Assert.Equal(black.DisplayName, picker.Text);
        Assert.Empty(commits);
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(regular.DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Redo());
        Flush(context.Window);
        Assert.Equal(black.Variant, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style.FontVariant);
        Assert.True(Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style.Bold);
        Assert.Equal(black.DisplayName, picker.Text);
    }

    [AvaloniaTheory(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormattingClickCommitsThePendingFontIdentityInTheSameTransaction(bool italic)
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        var semiBold = Variant(context.Session.Fonts.Candidates, 600);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "Pending face 字体");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { FontFamily = "sans-serif", FontVariant = null, Bold = false, Italic = false }
        });
        context.Session.SelectCue(cueId);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(cueId);
        var picker = UiTestActions.Find<FontFamilyPicker>(context.Window, "FontCombo");
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        input.BringIntoView();
        Flush(context.Window);
        Assert.True(input.Focus());
        input.SelectAll();
        context.Window.KeyTextInput(semiBold.DisplayName);
        Flush(context.Window);
        Assert.Equal(semiBold.DisplayName, picker.Text);
        Assert.Same(original, context.Session.DocumentSnapshot);
        var toggleName = italic ? "ItalicCheck" : "BoldCheck";
        var toggle = UiTestActions.Find<ToolbarToggleButton>(context.Window, toggleName);
        toggle.BringIntoView();
        Flush(context.Window);
        var target = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), context.Window);
        var wasOpen = picker.IsDropDownOpen;
        var filtered = picker.FontCandidates.Where(candidate => picker.ItemFilter!(picker.Text, candidate))
            .Select(candidate => candidate.DisplayName).ToArray();
        var clicks = 0;
        toggle.Click += (_, _) => clicks++;
        TestContext.Current.TestOutputHelper?.WriteLine($"Before {toggleName}: dropdown={wasOpen}, filtered={filtered.Length}, " +
            $"target={target}, bounds={toggle.Bounds}, inputFocused={input.IsFocused}, canUndo={context.Session.Editor.CanUndo}; " +
            string.Join(" | ", filtered));
        var trace = TraceFontHistory(context, "Styles " + toggleName);
        WriteFontHistoryState(context, "Styles before click");
        context.Session.Editor.Changed += trace;
        try
        {
            UiTestActions.Click(context.Window, toggleName);
            Flush(context.Window);
        }
        finally
        {
            context.Session.Editor.Changed -= trace;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"After {toggleName}: clicks={clicks}, checked={toggle.IsChecked}, " +
            $"dropdown={picker.IsDropDownOpen}, draft={picker.Text}, canUndo={context.Session.Editor.CanUndo}");
        Assert.True(clicks == 1, $"Physical {toggleName} click did not reach the checkbox; " +
            $"dropdown was {wasOpen}, matching candidates={filtered.Length}, target={target}, bounds={toggle.Bounds}.");
        var style = Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style;
        Assert.Equal("Noto Sans SC", style.FontFamily);
        Assert.Equal(italic ? 600 : 700, style.FontVariant?.Weight);
        Assert.Equal(italic, style.Italic);
        Assert.Equal(!italic, style.Bold);
        Assert.Equal(Variant(picker.FontCandidates, italic ? 600 : 700).DisplayName, picker.Text);
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        WriteFontHistoryState(context, "Styles after single Undo");
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal("sans-serif", picker.Text);
        Assert.True(context.Session.Editor.Redo());
        Flush(context.Window);
        Assert.Equal(style, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style);
        Assert.Equal("Noto Sans SC", picker.CurrentFont.FamilyName);
    }

    [AvaloniaTheory(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailsFormattingClickCombinesThePendingFontAndSelectedCharacterInOneUndo(bool italic)
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        var semiBold = Variant(context.Session.Fonts.Candidates, 600);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { FontFamily = "sans-serif", FontVariant = null, Bold = false, Italic = false }
        });
        context.Session.SelectCue(cueId);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(cueId);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 1);
        Flush(host);
        var picker = UiTestActions.Find<FontFamilyPicker>(host, "SelectionFontInput");
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        input.BringIntoView();
        Flush(host);
        Assert.True(input.Focus());
        input.SelectAll();
        host.KeyTextInput(semiBold.DisplayName);
        Flush(host);
        Assert.Equal(semiBold.DisplayName, picker.Text);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Details.StyleDraft.IsDirty);
        var buttonName = italic ? "ItalicSelectionButton" : "BoldSelectionButton";
        var clicks = 0;
        UiTestActions.Find<Button>(host, buttonName).Click += (_, _) => clicks++;
        var trace = TraceFontHistory(context, "Details " + buttonName);
        WriteFontHistoryState(context, "Details before click");
        context.Session.Editor.Changed += trace;
        try
        {
            UiTestActions.Click(host, buttonName);
            Flush(host);
        }
        finally
        {
            context.Session.Editor.Changed -= trace;
        }
        Assert.Equal(1, clicks);
        var line = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        var span = Assert.Single(line.InlineSpans);
        Assert.Equal(0, span.Utf16Start);
        Assert.Equal(1, span.Utf16Length);
        var style = span.Style.ApplyTo(line.Style);
        Assert.Equal("Noto Sans SC", style.FontFamily);
        Assert.Equal(italic ? 600 : 700, style.FontVariant?.Weight);
        Assert.Equal(italic, style.Italic);
        Assert.Equal(!italic, style.Bold);
        Assert.Equal(original.Subtitles[0].Style, line.Style);
        Assert.Equal(Variant(picker.FontCandidates, italic ? 600 : 700).DisplayName, picker.Text);
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        WriteFontHistoryState(context, "Details after single Undo");
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal("sans-serif", picker.Text);
        Assert.True(context.Session.Editor.Redo());
        Flush(host);
        line = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(style, Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style));
        Assert.Equal("Noto Sans SC", picker.CurrentFont.FamilyName);
    }

    [AvaloniaTheory(SkipUnless = nameof(HasMacNotoFonts), Skip = "Requires macOS with installed Noto Sans SC in the system font catalog.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingAFormattingPointerStillCommitsTheFontBlurOnce(bool details)
    {
        await using var context = new MainWindowTestContext();
        await WaitForFontsAsync(context);
        var semiBold = Variant(context.Session.Fonts.Candidates, 600);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.Editor.UpdateSubtitle(cueId, line => line with
        {
            Style = line.Style with { FontFamily = "sans-serif", FontVariant = null, Bold = false, Italic = false }
        });
        context.Session.SelectCue(cueId);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(cueId);
        Window host = context.Window;
        if (details)
        {
            await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
            host = Assert.Single(context.Window.Layouts.FloatingWindows);
            host.Width = 950;
            host.Height = 1200;
            UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        }
        var picker = UiTestActions.Find<FontFamilyPicker>(host, details ? "SelectionFontInput" : "FontCombo");
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        input.BringIntoView();
        Flush(host);
        Assert.True(input.Focus());
        input.SelectAll();
        host.KeyTextInput(semiBold.DisplayName);
        Flush(host);
        Assert.Same(original, context.Session.DocumentSnapshot);
        var bold = UiTestActions.Find<ToggleButton>(host, details ? "BoldSelectionButton" : "BoldCheck");
        bold.BringIntoView();
        Flush(host);
        var clicks = 0;
        bold.Click += (_, _) => clicks++;
        var point = bold.TranslatePoint(new Point(bold.Bounds.Width / 2, bold.Bounds.Height / 2), host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        Flush(host);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        var outside = new Point(4, 4);
        host.MouseMove(outside);
        host.MouseUp(outside, MouseButton.Left);
        Flush(host);
        Assert.Equal(0, clicks);
        Assert.False(bold.IsChecked);
        var edited = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        var style = details ? Assert.Single(edited.InlineSpans).Style.ApplyTo(edited.Style) : edited.Style;
        Assert.Equal(semiBold.FamilyName, style.FontFamily);
        Assert.Equal(semiBold.Variant, style.FontVariant);
        Assert.False(style.Bold);
        Assert.False(style.Italic);
        Assert.Equal(semiBold.DisplayName, picker.Text);
        if (details)
        {
            Assert.Equal(original.Subtitles[0].Style, edited.Style);
            Assert.False(context.Session.Details.StyleDraft.IsDirty);
        }
        Assert.True(context.Session.Editor.Undo());
        Flush(host);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal("sans-serif", picker.Text);
        Assert.True(context.Session.Editor.Redo());
        Flush(host);
        Assert.Equal(semiBold.Variant, picker.CurrentFont.Variant);
    }

    private static EventHandler TraceFontHistory(MainWindowTestContext context, string scope)
    {
        var changes = 0;
        return (_, _) => WriteFontHistoryState(context, $"{scope} Editor.Changed #{++changes}");
    }

    private static void WriteFontHistoryState(MainWindowTestContext context, string phase)
    {
        var editor = context.Session.Editor;
        var line = Assert.Single(editor.Snapshot.Subtitles);
        var first = line.InlineSpans.FirstOrDefault(span => span.Utf16Start == 0);
        var effective = first?.Style.ApplyTo(line.Style) ?? line.Style;
        TestContext.Current.TestOutputHelper?.WriteLine($"{phase}: undoLabel={editor.UndoLabel ?? "<none>"}, " +
            $"redoLabel={editor.RedoLabel ?? "<none>"}, canUndo={editor.CanUndo}, family={effective.FontFamily}, " +
            $"variant={effective.FontVariant?.Name ?? "<none>"}, weight={effective.FontVariant?.Weight.ToString(CultureInfo.InvariantCulture) ?? "<none>"}, " +
            $"bold={effective.Bold}, italic={effective.Italic}, baseFamily={line.Style.FontFamily}, " +
            $"baseWeight={line.Style.FontVariant?.Weight.ToString(CultureInfo.InvariantCulture) ?? "<none>"}");
    }

    private static async Task WaitForFontsAsync(MainWindowTestContext context)
    {
        await context.Session.ApplicationContext.Initialization;
        await context.Session.Fonts.EnsureLoadedAsync();
        Flush(context.Window);
    }

    private static FontSelection Variant(IEnumerable<FontPickerCandidate> candidates, int weight) =>
        Assert.Single(candidates, value => value.Selection.FamilyName == "Noto Sans SC" &&
            value.Selection.Variant is { } variant && variant.Weight == weight && variant.Width == 5 && !variant.Italic).Selection;

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
