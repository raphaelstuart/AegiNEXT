using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace AegiNext.Desktop.Controls;

/// <summary>仅提供九个非拉伸锚点的复用视图；通过标准按钮保留键盘与无障碍操作。</summary>
public sealed class AnchorPresetPicker : UserControl
{
    private readonly AnchorPresetGlyph[] glyphs = new AnchorPresetGlyph[9];
    private readonly AnchorPresetButton[] buttons = new AnchorPresetButton[9];
    private readonly List<IDisposable> localizationBindings = [];
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
            glyphs[index] = glyph;
            buttons[index] = button;
        }
        Content = grid;
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

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        for (var index = 0; index < buttons.Length; index++)
        {
            var textKey = "Workbench." + names[index];
            var label = Localization.Observe(() => PresetLabel(textKey));
            var tooltip = Localization.Observe(() => PresetLabel(textKey) + Environment.NewLine + Localization.Get("Workbench.AnchorPresetHint"));
            localizationBindings.Add(buttons[index].Bind(ToolTip.TipProperty, tooltip.ToBinding()));
            localizationBindings.Add(buttons[index].Bind(AutomationProperties.NameProperty, label.ToBinding()));
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var binding in localizationBindings)
        {
            binding.Dispose();
        }
        localizationBindings.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private static string PresetLabel(string textKey)
    {
        return Localization.Get("Workbench.AnchorPreset") + " · " + Localization.Get(textKey);
    }
}
