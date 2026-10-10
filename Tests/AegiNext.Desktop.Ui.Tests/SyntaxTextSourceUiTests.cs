using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SyntaxTextSourceUiTests
{
    [AvaloniaFact]
    public void DenseStylesSupportRandomAccessWithBoundedLookupWork()
    {
        const int COUNT = 20000;
        var plain = new GenericTextRunProperties(new(FontFamily.Default), 13, foregroundBrush: Brushes.Black);
        var colored = new GenericTextRunProperties(new(FontFamily.Default), 13, foregroundBrush: Brushes.Red);
        var styles = new CountingTextStyles(Enumerable.Range(0, COUNT)
            .Select(index => new ValueSpan<TextRunProperties>(index, 1, colored)).ToArray());
        var source = new SyntaxTextSource(new string('a', COUNT), plain, styles);
        for (var index = COUNT - 1; index >= 0; index--)
        {
            var run = Assert.IsType<TextCharacters>(source.GetTextRun(index));
            Assert.Equal("a", run.Text.ToString());
            Assert.Same(colored, run.Properties);
        }

        Assert.True(styles.Reads <= COUNT * 16, $"Dense styles required {styles.Reads} lookups.");
        Assert.Null(source.GetTextRun(COUNT));
    }

    [AvaloniaFact]
    public void ExplicitDefaultStylesAndUnstyledGapsKeepTheirFullRunLengths()
    {
        var plain = new GenericTextRunProperties(new(FontFamily.Default), 13, foregroundBrush: Brushes.Black);
        var colored = new GenericTextRunProperties(new(FontFamily.Default), 13, foregroundBrush: Brushes.Red);
        var text = "a" + new string('x', 200) + "z";
        var explicitSource = new SyntaxTextSource(text, plain, [new(0, 1, colored), new(1, 200, plain), new(201, 1, colored)]);
        var gapSource = new SyntaxTextSource(text, plain, [new(0, 1, colored), new(201, 1, colored)]);
        foreach (var source in new[] { explicitSource, gapSource })
        {
            var run = Assert.IsType<TextCharacters>(source.GetTextRun(1));
            Assert.Equal(new string('x', 200), run.Text.ToString());
            Assert.Same(plain, run.Properties);
            Assert.Equal("z", Assert.IsType<TextCharacters>(source.GetTextRun(201)).Text.ToString());
        }
    }

    [AvaloniaFact]
    public void StyleBoundariesPreserveCompleteEmojiAndCombiningGraphemes()
    {
        const string TEXT = "😀e\u0301中";
        var plain = new GenericTextRunProperties(new(FontFamily.Default), 13, foregroundBrush: Brushes.Black);
        var colored = new GenericTextRunProperties(new(FontFamily.Default), 13, foregroundBrush: Brushes.Red);
        var source = new SyntaxTextSource(TEXT, plain,
            [new(0, 1, colored), new(1, 2, plain), new(3, 1, colored), new(4, 1, plain)]);
        Assert.Equal("😀", Assert.IsType<TextCharacters>(source.GetTextRun(0)).Text.ToString());
        Assert.Equal("e\u0301", Assert.IsType<TextCharacters>(source.GetTextRun(2)).Text.ToString());
        Assert.Equal("中", Assert.IsType<TextCharacters>(source.GetTextRun(4)).Text.ToString());
    }

    [AvaloniaFact]
    public void DenseEffectSyntaxRemainsColoredWithoutDroppingAnySourceCharacters()
    {
        var source = string.Concat(Enumerable.Repeat("at 0 opacity 1 linear ", 5000)) + "# 中文 😀";
        var presenter = new EffectScriptTextPresenter { Text = source };
        var window = new Window { Width = 500, Height = 200, Content = presenter };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var runs = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).ToArray();
            Assert.Equal(source, string.Concat(runs.Select(run => run.Text.ToString())));
            Assert.True(runs.Select(run => (run.Properties?.ForegroundBrush as ISolidColorBrush)?.Color).Distinct().Count() >= 3);
            Assert.Equal(source, presenter.Text);
        }
        finally
        {
            window.Close();
        }
    }
}
