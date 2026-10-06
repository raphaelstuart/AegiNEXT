using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Subtitles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleListLayoutUiTests
{
    [AvaloniaTheory]
    [InlineData(640, "en-US", false)]
    [InlineData(640, "en-US", true)]
    [InlineData(640, "zh-CN", false)]
    [InlineData(640, "zh-CN", true)]
    [InlineData(1100, "en-US", false)]
    [InlineData(1100, "en-US", true)]
    [InlineData(1100, "zh-CN", false)]
    [InlineData(1100, "zh-CN", true)]
    public async Task ColumnHeadersAlignWithRowsBeforeAndAfterScrolling(double width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        using var view = new SubtitlesPanelView(context.ViewModel.Subtitles, context.Session);
        var window = new Window
        {
            Width = width,
            Height = 340,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            foreach (var count in new[] { 2, 40, 2 })
            {
                var document = CreateDocument(count);
                context.Session.Editor.Reset(document);
                Flush(window);
                var list = view.FindControl<ListBox>("SubtitleList")!;
                var header = view.GetVisualDescendants().OfType<Grid>().Single(grid =>
                    grid.Children.OfType<TextBlock>().Any(text => text.Text == Localization.Get("Workbench.SubtitleType")));
                AssertRowAlignment(window, header, list, document.Subtitles[0].Id);
                if (count > 2)
                {
                    list.ScrollIntoView(context.ViewModel.Subtitles.VisibleRows[^1]);
                    Flush(window);
                    AssertRowAlignment(window, header, list, document.Subtitles[^1].Id);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectDocument CreateDocument(int count)
    {
        var lines = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Text = "字幕 ABC 123",
            Start = new(index * 3),
            End = new(index * 3 + 2)
        }).ToImmutableArray();
        return new()
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray()
        };
    }

    private static void AssertRowAlignment(Window window, Grid header, ListBox list, Guid id)
    {
        var row = list.GetVisualDescendants().OfType<Grid>().Single(grid =>
            grid.DataContext is SubtitleRow row && row.Id == id && grid.Children.OfType<TextBox>().Count() == 3);
        foreach (var label in header.Children.OfType<TextBlock>())
        {
            var column = Grid.GetColumn(label);
            var field = row.Children.Single(control => Grid.GetColumn(control) == column);
            var labelPoint = label.TranslatePoint(new Point(column == 4 ? 0 : label.Bounds.Width / 2, 0), window)!.Value;
            var fieldPoint = field.TranslatePoint(new Point(column == 4 ? 0 : field.Bounds.Width / 2, 0), window)!.Value;
            Assert.True(Math.Abs(labelPoint.X - fieldPoint.X) <= 0.5,
                $"Column {column}: header X={labelPoint.X}, field X={fieldPoint.X}.");
        }

        var content = row.Children.OfType<TextBox>().Single(box => Grid.GetColumn(box) == 4);
        var right = content.TranslatePoint(new Point(content.Bounds.Width, 0), list)!.Value.X;
        Assert.InRange(right, 0, list.Bounds.Width);
        Assert.True(content.Bounds.Width > 0);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
