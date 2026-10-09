using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class FontPickerVariantUiTests
{
    private static readonly string[] orderedFamilies = ["Fixture", "Alphabetical First", "Zebra Family"];
    private static readonly string[] orderedVariants = ["Regular", "SemiBold", "Bold", "Black"];

    [AvaloniaFact]
    public void CurrentVariantWithoutPostScriptMetadataDoesNotAddASecondWeight()
    {
        var picker = CreatePicker();
        var stored = Candidate("Regular", 400).Selection;
        stored = new(stored.FamilyName, stored.Variant!.Value with { PostScriptName = null }, true);
        picker.SetCurrentFont(stored);
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            var regular = Assert.Single(family.Items.Cast<MenuItem>(), item => Equals(item.Header, "Regular"));
            Assert.Equal(4, family.Items.Count);
            Assert.True(regular.IsChecked);
            Assert.Equal(Candidate("Regular", 400).Selection, Assert.IsType<FontPickerCandidate>(regular.Tag).Selection);
            Assert.Equal(stored, picker.CurrentFont);
            Assert.Empty(values);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AFamilyWithOneVariantCommitsThatVariantWithoutOpeningASubmenu()
    {
        var picker = new FontFamilyPicker { Width = 300 };
        var only = Candidate("Black", 900);
        picker.RefreshFontCandidates([only]);
        picker.SetCurrentFamily("Existing family");
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = GetFontMenu(picker).Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Fixture"));
            Assert.False(family.HasSubMenu);
            ClickMenuItem(family);
            Flush(window);
            Assert.Equal(only.Selection, Assert.Single(values));
            Assert.Equal(only.Selection, picker.CurrentFont);
            Assert.Equal(only.DisplayName, picker.Text);
            Assert.False(picker.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClosingTheDropdownCannotReplaceTheVariantChosenByThePointer()
    {
        var picker = CreatePicker();
        var current = picker.CurrentFont;
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        picker.DropDownClosed += (_, _) => picker.SetCurrentFont(current);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            ClickMenuItem(family);
            Flush(window);
            ClickMenuItem(family.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Black")));
            Flush(window);
            Assert.Equal(Candidate("Black", 900).Selection, Assert.Single(values));
            Assert.Equal(Candidate("Black", 900).Selection, picker.CurrentFont);
            Assert.Equal("Fixture Black", picker.Text);
            Assert.False(picker.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RefreshingTheSameCatalogKeepsTheOpenSubmenuAndItsPointerTargets()
    {
        var picker = CreatePicker();
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var menu = GetFontMenu(picker);
            var family = Assert.Single(menu.Items.Cast<MenuItem>());
            ClickMenuItem(family);
            Flush(window);
            var black = family.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Black"));
            picker.RefreshFontCandidates(Candidates());
            Flush(window);
            Assert.Same(family, Assert.Single(menu.Items.Cast<MenuItem>()));
            Assert.True(family.IsSubMenuOpen);
            Assert.True(black.IsAttachedToVisualTree());
            Assert.Empty(values);
            ClickMenuItem(black);
            Flush(window);
            Assert.Equal(Candidate("Black", 900).Selection, Assert.Single(values));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingTheCurrentVariantKeepsItsCheckmarkWhenTheMenuReopens()
    {
        VerifyCurrentCheckmarkAfterReopening(false);
    }

    [AvaloniaFact]
    public void ClickingTheCurrentSingleVariantKeepsItsFamilyCheckmarkWhenTheMenuReopens()
    {
        VerifyCurrentCheckmarkAfterReopening(true);
    }

    [AvaloniaFact]
    public void SwitchingFromAMissingVariantRemovesItsTemporaryCandidate()
    {
        var picker = CreatePicker();
        var missing = new FontSelection("Fixture", new() { Name = "Designer Heavy", Weight = 913 });
        picker.SetCurrentFont(missing);
        Assert.Contains(picker.FontCandidates, candidate => candidate.Selection == missing);
        picker.SetCurrentFont(Candidate("Black", 900).Selection);
        Assert.DoesNotContain(picker.FontCandidates, candidate => candidate.Selection == missing);
        Assert.Equal(4, picker.FontCandidates.Count(candidate => candidate.Selection.Variant is not null));
    }

    [AvaloniaFact]
    public void PointerSelectionOpensTheSubmenuWithoutCommittingTheSearchDraft()
    {
        var picker = CreatePicker();
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("Pending custom family");
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            ClickMenuItem(family);
            Flush(window);
            Assert.True(family.IsSubMenuOpen);
            Assert.True(picker.IsDropDownOpen);
            Assert.Equal("Pending custom family", picker.Text);
            Assert.Empty(values);
            var black = family.Items.Cast<MenuItem>().Single(item => Equals(item.Header, "Black"));
            ClickMenuItem(black);
            Flush(window);
            Assert.Equal(Candidate("Black", 900).Selection, Assert.Single(values));
            Assert.False(picker.IsDropDownOpen);
            Assert.Equal("Fixture Black", picker.Text);
            picker.OpenFontList();
            Flush(window);
            family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            var current = family.Items.Cast<MenuItem>().First();
            Assert.Equal("Black", current.Header);
            Assert.True(current.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AFamilyWithoutVariantsCanBeSelectedDirectly()
    {
        var picker = CreatePicker();
        picker.RefreshFontFamilies(["Embedded Project Family"]);
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = GetFontMenu(picker).Items.Cast<MenuItem>()
                .Single(item => Equals(item.Header, "Embedded Project Family"));
            Assert.False(family.HasSubMenu);
            ClickMenuItem(family);
            Flush(window);
            Assert.Equal(new("Embedded Project Family"), Assert.Single(values));
            Assert.False(picker.IsDropDownOpen);
            Assert.Equal("Embedded Project Family", picker.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FontDropdownGroupsWeightsAndMovesTheCurrentFamilyToTheFirstRow()
    {
        var picker = CreatePicker();
        picker.RefreshFontCandidates(Candidates().Concat(
        [
            new FontPickerCandidate(new("Alphabetical First")),
            new FontPickerCandidate(new("Zebra Family"))
        ]));
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var menu = GetFontMenu(picker);
            var families = menu.Items.Cast<MenuItem>().ToArray();
            Assert.Equal(orderedFamilies, families.Select(value => value.Header));
            var variants = families[0].Items.Cast<MenuItem>().ToArray();
            Assert.Equal(orderedVariants, variants.Select(value => value.Header));
            Assert.True(families[0].IsChecked);
            Assert.True(variants[0].IsChecked);
            Assert.Equal("Fixture Regular", picker.Text);
            Assert.Empty(values);

            picker.IsDropDownOpen = false;
            picker.SetCurrentFamily("Zebra Family");
            picker.OpenFontList();
            Flush(window);
            Assert.Equal("Zebra Family", menu.Items.Cast<MenuItem>().First().Header);
            Assert.Single(menu.Items.Cast<MenuItem>(), value => Equals(value.Header, "Zebra Family"));
            Assert.Empty(values);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OpeningAWeightSubmenuPreservesTheDraftAndCommitsOnlyTheChosenVariant()
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
            picker.OpenFontList();
            Flush(window);
            UiTestActions.Press(window, Key.Down);
            Assert.Equal(0, GetFontMenu(picker).SelectedIndex);
            UiTestActions.Press(window, Key.Right);
            Flush(window);
            var family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            Assert.True(family.IsSubMenuOpen,
                $"Dropdown: {picker.IsDropDownOpen}; selected: {GetFontMenu(picker).SelectedItem}; focus: {window.FocusManager.GetFocusedElement()}; text: {picker.Text}");
            Assert.True(picker.IsDropDownOpen);
            Assert.Equal("Fixture Regular", picker.Text);
            Assert.Empty(values);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            Assert.Equal(Candidate("SemiBold", 600).Selection, Assert.Single(values));
            Assert.Equal("Fixture SemiBold", picker.Text);
            Assert.False(picker.IsDropDownOpen);
            Assert.False(family.IsSubMenuOpen);
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
    public void MissingCurrentVariantIsPinnedAndMenuDismissalDoesNotCommitIt()
    {
        var picker = CreatePicker();
        var missing = new FontSelection("Unavailable Family", new() { Name = "Designer Heavy", Weight = 913 });
        picker.SetCurrentFont(missing);
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = GetFontMenu(picker).Items.Cast<MenuItem>().First();
            Assert.Equal("Unavailable Family", family.Header);
            Assert.False(family.HasSubMenu);
            Assert.Equal(missing, Assert.IsType<FontPickerCandidate>(family.Tag).Selection);
            UiTestActions.Press(window, Key.Down);
            UiTestActions.Press(window, Key.Right);
            UiTestActions.Press(window, Key.Escape);
            UiTestActions.Press(window, Key.Escape);
            Flush(window);
            Assert.False(picker.IsDropDownOpen);
            Assert.Equal(missing, picker.CurrentFont);
            Assert.Equal(missing.DisplayName, picker.Text);
            Assert.Empty(values);
        }
        finally
        {
            window.Close();
        }
    }

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
            UiTestActions.Press(window, Key.Right);
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

    private static void VerifyCurrentCheckmarkAfterReopening(bool singleVariant)
    {
        var picker = CreatePicker();
        if (singleVariant)
        {
            picker.RefreshFontCandidates([Candidate("Regular", 400)]);
        }
        var window = CreateWindow(picker, new Button());
        var values = new List<FontSelection>();
        picker.FamilyCommitted += (_, value) => values.Add(value.Selection);
        try
        {
            window.Show();
            Flush(window);
            picker.OpenFontList();
            Flush(window);
            var family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            ClickMenuItem(family);
            Flush(window);
            if (!singleVariant)
            {
                ClickMenuItem(family.Items.Cast<MenuItem>().Single(item => item.IsChecked));
                Flush(window);
            }
            Assert.False(picker.IsDropDownOpen);
            picker.OpenFontList();
            Flush(window);
            family = Assert.Single(GetFontMenu(picker).Items.Cast<MenuItem>());
            Assert.True(family.IsChecked);
            if (!singleVariant)
            {
                Assert.Equal("Regular", Assert.Single(family.Items.Cast<MenuItem>(), item => item.IsChecked).Header);
            }
            Assert.Empty(values);
        }
        finally
        {
            window.Close();
        }
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

    private static MenuBase GetFontMenu(FontFamilyPicker picker)
    {
        return Assert.IsAssignableFrom<MenuBase>(Assert.Single(picker.GetVisualDescendants().OfType<Popup>()).Child);
    }

    private static void ClickMenuItem(MenuItem item)
    {
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
        root.UpdateLayout();
        var point = item.TranslatePoint(new(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
