using AegiNext.Core.Projects;
using AegiNext.Desktop.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace AegiNext.Desktop.Controls;

/// <summary>仅提供九个非拉伸锚点的复用视图；通过标准按钮保留键盘与无障碍操作。</summary>
public sealed class AnchorPresetPicker : UserControl
{
    private readonly AnchorPresetButton[] buttons = new AnchorPresetButton[9];
    private readonly AnchorPresetGlyph[] glyphs = new AnchorPresetGlyph[9];
    private static readonly string[] names =
    [
        "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
        "BottomLeft", "BottomCenter", "BottomRight"
    ];

    /// <summary>构造九宫格，每个格子显示父画布和对应单点 anchor。</summary>
    public AnchorPresetPicker()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new("Auto,Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto"),
            ColumnSpacing = 6, RowSpacing = 6, HorizontalAlignment = HorizontalAlignment.Left
        };
        for (var index = 0; index < 9; index++)
        {
            var anchor = new ScenePoint(index % 3 / 2d, index / 3 / 2d);
            var glyph = new AnchorPresetGlyph { Anchor = anchor, Width = 32, Height = 28 };
            var button = new AnchorPresetButton
            {
                Name = $"AnchorPreset{names[index]}", Content = glyph, Padding = new(6), MinHeight = 0
            };
            glyph.Bind(AnchorPresetGlyph.ForegroundProperty, button.GetObservable(Button.ForegroundProperty));
            button.Click += (_, _) => PresetSelected?.Invoke(this, new(anchor,
                button.SelectionModifiers.HasFlag(KeyModifiers.Shift), button.SelectionModifiers.HasFlag(KeyModifiers.Alt)));
            Grid.SetColumn(button, index % 3);
            Grid.SetRow(button, index / 3);
            grid.Children.Add(button);
            buttons[index] = button;
            glyphs[index] = glyph;
        }
        Content = grid;
        RefreshLanguage();
    }

    public event EventHandler<AnchorPresetSelectionEventArgs>? PresetSelected;

    /// <summary>显示当前 anchor 与 pivot；连续数值无需强制吸附到预设。</summary>
    public void SetSelection(ScenePoint? anchor, ScenePoint? pivot)
    {
        foreach (var glyph in glyphs)
        {
            glyph.IsAnchorSelected = anchor is { } current && current == glyph.Anchor;
            glyph.IsPivotSelected = pivot is { } currentPivot && currentPivot == glyph.Anchor;
        }
    }

    /// <summary>刷新按钮提示与辅助名称，保持当前选择和键盘焦点。</summary>
    public void RefreshLanguage()
    {
        for (var index = 0; index < buttons.Length; index++)
        {
            var label = $"{WorkbenchText.Get("AnchorPreset")} · {WorkbenchText.Get(names[index])}";
            ToolTip.SetTip(buttons[index], label);
            AutomationProperties.SetName(buttons[index], label);
        }
    }
}
