using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls.Common;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleColorTagMenuUiTests
{
    [AvaloniaTheory]
    [InlineData("Review", "#AABBCC", true)]
    [InlineData("Review", "#aabbcc", true)]
    [InlineData("Reviewed", "#AABBCC", false)]
    [InlineData("Review", "#112233", true)]
    [InlineData("review", "#AABBCC", false)]
    public async Task FlatMenuMergesMatchingNamesAndSeparatesDifferentProjectNames(string name, string color, bool matching)
    {
        using var environment = new UiTestEnvironment();
        using var menu = new SubtitleColorTagMenu("ColorTags");
        var personal = new SubtitleColorTag { Name = "Review", ColorHex = "#AABBCC" };
        var project = new SubtitleColorTag { Name = name, ColorHex = color };
        Guid? appliedProject = null;
        var imports = 0;
        menu.Refresh([project], [personal], project.Id, true, () => true,
            id =>
            {
                appliedProject = id;
                return Task.CompletedTask;
            },
            _ =>
            {
                imports++;
                return Task.CompletedTask;
            }, () => Task.CompletedTask);

        var tags = ColorItems(menu);
        Assert.Equal(matching ? 1 : 2, tags.Length);
        Assert.All(menu.Item.Items.OfType<MenuItem>(), item => Assert.Empty(item.Items));
        Assert.All(tags, tag => Assert.NotNull(tag.Icon));
        var selected = Assert.Single(tags, item => item.IsChecked);
        Assert.Equal("SubtitleColorTagProject" + project.Id.ToString("N"), selected.Name);
        Assert.Equal(Color.Parse(project.ColorHex), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Ellipse>(selected.Icon).Fill).Color);
        if (!matching)
        {
            Assert.Equal(personal.Name, tags[0].Header);
            var items = menu.Item.Items.Cast<object>().ToArray();
            var first = Array.IndexOf(items, tags[0]);
            var second = Array.IndexOf(items, tags[1]);
            Assert.IsType<Separator>(Assert.Single(items.Skip(first + 1).Take(second - first - 1)));
        }
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(selected.Command).ExecuteAsync(null);
        Assert.Equal(project.Id, appliedProject);
        Assert.Equal(0, imports);
    }

    [AvaloniaFact]
    public async Task PersonalOnlyEntryImportsTheDefinitionAndDuplicateDefinitionsAppearOnce()
    {
        using var environment = new UiTestEnvironment();
        using var menu = new SubtitleColorTagMenu("ColorTags");
        var personal = new SubtitleColorTag { Name = "Ready", ColorHex = "#112233" };
        var project = new SubtitleColorTag { Name = "Review", ColorHex = "#AABBCC" };
        var duplicate = project with { Id = Guid.NewGuid(), ColorHex = "#778899" };
        SubtitleColorTag? imported = null;
        menu.Refresh([project, duplicate], [personal, personal with { Id = Guid.NewGuid() }], duplicate.Id, true,
            () => true, _ => Task.CompletedTask,
            value =>
            {
                imported = value;
                return Task.CompletedTask;
            }, () => Task.CompletedTask);

        var tags = ColorItems(menu);
        Assert.Equal(2, tags.Length);
        Assert.Equal("SubtitleColorTagPersonal" + personal.Id.ToString("N"), tags[0].Name);
        Assert.Equal("SubtitleColorTagProject" + duplicate.Id.ToString("N"), tags[1].Name);
        Assert.True(tags[1].IsChecked);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(tags[0].Command).ExecuteAsync(null);
        Assert.Same(personal, imported);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleSourceMenuHasNoEmptyCategoryOrAdjacentSeparators(bool personal)
    {
        using var environment = new UiTestEnvironment();
        using var menu = new SubtitleColorTagMenu("ColorTags");
        var tag = new SubtitleColorTag { Name = "Review", ColorHex = "#112233" };
        menu.Refresh(personal ? [] : [tag], personal ? [tag] : [], null, true, () => true,
            _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask);

        Assert.Single(ColorItems(menu));
        var items = menu.Item.Items.Cast<object>().ToArray();
        Assert.Equal(5, items.Length);
        Assert.IsType<MenuItem>(items[0]);
        Assert.IsType<Separator>(items[1]);
        Assert.IsType<MenuItem>(items[2]);
        Assert.IsType<Separator>(items[3]);
        Assert.IsType<MenuItem>(items[4]);
    }

    private static MenuItem[] ColorItems(SubtitleColorTagMenu menu) => menu.Item.Items.OfType<MenuItem>()
        .Where(item => item.Name?.StartsWith("SubtitleColorTag", StringComparison.Ordinal) == true).ToArray();
}
