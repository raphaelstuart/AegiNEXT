using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class FontPickerVariantUiTests
{
    [AvaloniaFact]
    public void ClosingTheFontDropdownPassesTheOutsideClickToItsTarget()
    {
        var picker = CreatePicker();
        var button = new Button { Name = "OutsideFontButton", Content = "Outside target", Margin = new(0, 150, 0, 0) };
        var window = CreateWindow(picker, button);
        var clicks = 0;
        var values = new List<FontSelection>();
        button.Click += (_, _) => clicks++;
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("Fixture SemiBold");
            Flush(window);
            Assert.True(picker.IsDropDownOpen);
            Assert.Empty(values);
            UiTestActions.Click(window, "OutsideFontButton");
            Flush(window);
            Assert.Equal(1, clicks);
            Assert.False(picker.IsDropDownOpen);
            Assert.Equal(600, Assert.Single(values).Variant?.Weight);
            Assert.Equal("Fixture SemiBold", picker.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LegacyWeightFamilyAliasesDoNotMatchOtherVariantsOrTheOrdinaryFamily()
    {
        var aliases = new[] { "Fixture Black", "Fixture-SemiBold", "Localized Family" };
        FontPickerCandidate[] candidates =
        [
            new(new("Fixture", isSystemFont: true), aliases),
            new(Candidate("Regular", 400).Selection, aliases),
            new(Candidate("SemiBold", 600).Selection, aliases),
            new(Candidate("Black", 900).Selection, aliases)
        ];
        var picker = new FontFamilyPicker();
        picker.RefreshFontCandidates(candidates);
        var black = Assert.Single(picker.FontCandidates, candidate => picker.ItemFilter!("Black", candidate));
        Assert.Equal(900, black.Selection.Variant?.Weight);
        Assert.Equal(black, Assert.Single(picker.FontCandidates, candidate => picker.ItemFilter!("Heavy", candidate)));
        var semiBold = Assert.Single(picker.FontCandidates, candidate => picker.ItemFilter!("Fixture SemiBold", candidate));
        Assert.Equal(600, semiBold.Selection.Variant?.Weight);
        Assert.True(picker.ItemFilter!("Localized Family SemiBold", semiBold));
        var normalized = FontSelectionResolver.NormalizeCandidates(candidates);
        Assert.All(normalized, candidate => Assert.DoesNotContain(candidate.Aliases, alias => alias is "Fixture Black" or "Fixture-SemiBold"));
        Assert.True(FontSelectionResolver.TryResolve(normalized, default, "Localized Family SemiBold", out var selected));
        Assert.Equal(semiBold.Selection, selected);
    }

    [AvaloniaFact]
    public void VariantDisplayNamesTakePriorityOverLegacyFamilyAliases()
    {
        var black = Candidate("Black", 900);
        FontPickerCandidate[] candidates =
        [
            new(new("Fixture", isSystemFont: true), ["Fixture Black", "Fixture SemiBold"]),
            black,
            Candidate("SemiBold", 600)
        ];
        Assert.True(FontSelectionResolver.TryResolve(candidates, default, "Fixture Black", out var selection));
        Assert.Equal(black.Selection, selection);
        Assert.Equal(900, selection.Variant?.Weight);
        var picker = new FontFamilyPicker();
        picker.RefreshFontCandidates(candidates);
        picker.SetCurrentFamily("Fixture");
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        picker.Text = "Fixture Black";
        Assert.True(picker.CommitText());
        Assert.Equal(black.Selection, Assert.Single(values));
        Assert.Equal("Fixture Black", picker.Text);
    }

    [AvaloniaFact]
    public void NamedVariantSearchAndAliasesResolveTheActualDesignerName()
    {
        var picker = CreatePicker();
        var black = Assert.Single(picker.FontCandidates, value => value.Selection.Variant?.Weight == 900);
        var semiBold = Assert.Single(picker.FontCandidates, value => value.Selection.Variant?.Weight == 600);
        Assert.True(picker.ItemFilter!("Fixture Heavy", black));
        Assert.False(picker.ItemFilter("Heavy", semiBold));
        Assert.True(picker.ItemFilter("Localized Family SemiBold", semiBold));
        Assert.True(picker.TryResolveSelection("Fixture Heavy", out var selection));
        Assert.Equal(black.Selection, selection);
        Assert.Equal("Fixture Black", selection.DisplayName);
        Assert.Equal("Fixture-Black", selection.Variant?.PostScriptName);
        Assert.True(selection.IsSystemFont);
        Assert.True(picker.TryResolveSelection("Localized Family SemiBold", out selection));
        Assert.Equal(semiBold.Selection, selection);
        Assert.True(FontSelectionResolver.TryResolve(Candidates(), default, "Localized Family", out selection));
        Assert.Equal("Fixture", selection.FamilyName);
        Assert.Null(selection.Variant);
        Assert.True(selection.IsSystemFont);
    }

    [AvaloniaFact]
    public void SynchronizationAndCatalogRefreshKeepTheDraftWithoutSubmitting()
    {
        var picker = CreatePicker();
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        var missing = new FontSelection("Unavailable Family", new() { Name = "Designer Heavy", Weight = 913 });
        picker.SetCurrentFont(missing);
        Assert.Equal(missing.DisplayName, picker.Text);
        Assert.True(picker.CommitText());
        Assert.Empty(values);
        Assert.Equal(missing, picker.CurrentFont);
        picker.Text = "Future Custom Face";
        picker.RefreshFontCandidates(Candidates(), ["Imported Face"]);
        Assert.Equal("Future Custom Face", picker.Text);
        Assert.Contains("Imported Face", picker.FontFamilies);
        Assert.Empty(values);
        Assert.True(picker.CommitText());
        var committed = Assert.Single(values);
        Assert.Equal("Future Custom Face", committed.FamilyName);
        Assert.Null(committed.Variant);
        Assert.False(committed.IsSystemFont);
        Assert.True(picker.CommitText());
        Assert.Single(values);
    }

    [AvaloniaFact]
    public void EnterCommitsTheSelectedSystemVariantOnceAndLostFocusDoesNotRepeatIt()
    {
        var picker = CreatePicker();
        var button = new Button { Content = "Move focus" };
        var window = CreateWindow(picker, button);
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("Fixture Heavy");
            Flush(window);
            Assert.Empty(values);
            Assert.Equal("Fixture Heavy", picker.Text);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            var selected = Assert.Single(values);
            Assert.Equal("Fixture", selected.FamilyName);
            Assert.Equal("Black", selected.Variant?.Name);
            Assert.Equal(900, selected.Variant?.Weight);
            Assert.True(selected.IsSystemFont);
            Assert.Equal("Fixture Black", picker.Text);
            Assert.False(picker.IsDropDownOpen);
            Assert.True(button.Focus());
            Flush(window);
            Assert.Single(values);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeRestoresOnlyTheFontDraftAndDoesNotSubmit()
    {
        var picker = CreatePicker();
        var other = new TextBox { Text = "Other field draft" };
        var window = CreateWindow(picker, other);
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("Fixture Heavy");
            Flush(window);
            UiTestActions.Press(window, Key.Escape);
            Flush(window);
            Assert.Equal("Fixture Regular", picker.Text);
            Assert.Equal("Other field draft", other.Text);
            Assert.Equal("Regular", picker.CurrentFont.Variant?.Name);
            Assert.Empty(values);
            Assert.True(picker.CommitText());
            Assert.Empty(values);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LostFocusCommitsAValidCustomFamilyWhileInvalidTextRemainsEditable()
    {
        var picker = CreatePicker();
        var button = new Button { Content = "Move focus" };
        var window = CreateWindow(picker, button);
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("Uninstalled Custom Family");
            Flush(window);
            Assert.Empty(values);
            Assert.True(button.Focus());
            Flush(window);
            Assert.Equal("Uninstalled Custom Family", Assert.Single(values).FamilyName);
            Assert.Null(values[0].Variant);
            Assert.False(values[0].IsSystemFont);
            Assert.True(input.Focus());
            input.Text = string.Empty;
            Flush(window);
            Assert.True(button.Focus());
            Flush(window);
            Assert.Equal(string.Empty, picker.Text);
            Assert.Single(values);
            Assert.False(picker.CommitText());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FamilyCompatibilityEntryPreservesVariantCandidatesAndReturnsAnOrdinaryFamily()
    {
        var picker = CreatePicker();
        picker.RefreshFontFamilies(["Embedded Project Family"]);
        Assert.Contains("Embedded Project Family", picker.FontFamilies);
        Assert.Contains(picker.FontCandidates, value => value.Selection.Variant?.Weight == 900);
        picker.SetCurrentFamily("Embedded Project Family");
        Assert.Equal("Embedded Project Family", picker.Text);
        Assert.Null(picker.CurrentFont.Variant);
        Assert.False(picker.CurrentFont.IsSystemFont);
        Assert.True(picker.TryResolveSelection("Fixture", out var selected));
        Assert.Equal("Fixture", selected.FamilyName);
        Assert.Null(selected.Variant);
        Assert.True(selected.IsSystemFont);
    }

    private static FontFamilyPicker CreatePicker()
    {
        var picker = new FontFamilyPicker { Width = 300 };
        picker.RefreshFontCandidates(Candidates());
        picker.SetCurrentFont(Candidates()[0].Selection);
        return picker;
    }

    private static FontPickerCandidate[] Candidates() =>
    [
        Candidate("Regular", 400), Candidate("SemiBold", 600), Candidate("Bold", 700), Candidate("Black", 900)
    ];

    private static FontPickerCandidate Candidate(string name, int weight) => new(
        new("Fixture", new SubtitleFontVariant { Name = name, Weight = weight, PostScriptName = "Fixture-" + name }, true),
        ["Localized Family"]);

    private static Window CreateWindow(FontFamilyPicker picker, Control other) => new()
    {
        Width = 400,
        Height = 300,
        Content = new StackPanel { Children = { picker, other } }
    };

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
