using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Controls;

/// <summary>以水平和垂直两个独立按钮组选择字幕的九宫格对齐。</summary>
public sealed class SubtitleAlignmentPicker : UserControl
{
    /// <summary>字幕九宫格对齐索引；默认双向绑定。</summary>
    public static readonly StyledProperty<int> AlignmentIndexProperty =
        AvaloniaProperty.Register<SubtitleAlignmentPicker, int>(nameof(AlignmentIndex),
            (int)ProjectTextAlignment.BOTTOM_CENTER, defaultBindingMode: BindingMode.TwoWay,
            validate: value => Enum.IsDefined((ProjectTextAlignment)value));

    private readonly ToolbarToggleButton[] horizontalButtons = new ToolbarToggleButton[3];
    private readonly ToolbarToggleButton[] verticalButtons = new ToolbarToggleButton[3];
    private readonly List<IDisposable> localizationBindings = [];
    private static readonly string[] horizontalIcons = ["AlignLeft", "AlignCenter", "AlignRight"];
    private static readonly string[] verticalIcons = ["AlignTop", "AlignMiddle", "AlignBottom"];
    private static readonly string[] horizontalNames =
        ["HorizontalLeftButton", "HorizontalCenterButton", "HorizontalRightButton"];
    private static readonly string[] verticalNames =
        ["VerticalTopButton", "VerticalCenterButton", "VerticalBottomButton"];

    /// <summary>创建可换行的两个对齐按钮组。</summary>
    public SubtitleAlignmentPicker()
    {
        var horizontal = new StackPanel
        {
            Name = "HorizontalAlignmentGroup", Orientation = Orientation.Horizontal, Spacing = 4,
            Margin = new(0, 0, 12, 8)
        };
        var vertical = new StackPanel
        {
            Name = "VerticalAlignmentGroup", Orientation = Orientation.Horizontal, Spacing = 4,
            Margin = new(0, 0, 0, 8)
        };
        for (var index = 0; index < 3; index++)
        {
            var axisIndex = index;
            var horizontalButton = new ToolbarToggleButton
            {
                Name = horizontalNames[index], Content = WorkbenchIcon.Create(horizontalIcons[index])
            };
            horizontalButton.Click += (_, _) => CommitAlignment(AlignmentIndex / 3 * 3 + axisIndex);
            horizontal.Children.Add(horizontalButton);
            horizontalButtons[index] = horizontalButton;
            var verticalButton = new ToolbarToggleButton
            {
                Name = verticalNames[index], Content = WorkbenchIcon.Create(verticalIcons[index])
            };
            verticalButton.Click += (_, _) => CommitAlignment(axisIndex * 3 + AlignmentIndex % 3);
            vertical.Children.Add(verticalButton);
            verticalButtons[index] = verticalButton;
        }
        Content = new WrapPanel { Children = { horizontal, vertical } };
        UpdateSelection();
    }

    /// <summary>用户确认的九宫格对齐；重复点击选中项也会触发。</summary>
    public event EventHandler<SubtitleAlignmentChangedEventArgs>? AlignmentCommitted;

    /// <summary>字幕九宫格对齐索引，范围为零至八。</summary>
    public int AlignmentIndex
    {
        get => GetValue(AlignmentIndexProperty);
        set => SetValue(AlignmentIndexProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AlignmentIndexProperty)
        {
            UpdateSelection();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        for (var index = 0; index < 3; index++)
        {
            BindLabel(horizontalButtons[index], horizontalIcons[index]);
            BindLabel(verticalButtons[index], verticalIcons[index]);
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

    private void BindLabel(ToolbarToggleButton button, string icon)
    {
        var label = Localization.Observe("Workbench." + icon);
        localizationBindings.Add(button.Bind(ToolTip.TipProperty, label.ToBinding()));
        localizationBindings.Add(button.Bind(AutomationProperties.NameProperty, label.ToBinding()));
    }

    private void CommitAlignment(int alignment)
    {
        SetCurrentValue(AlignmentIndexProperty, alignment);
        UpdateSelection();
        AlignmentCommitted?.Invoke(this, new((ProjectTextAlignment)alignment));
    }

    private void UpdateSelection()
    {
        for (var index = 0; index < 3; index++)
        {
            horizontalButtons[index].IsChecked = index == AlignmentIndex % 3;
            verticalButtons[index].IsChecked = index == AlignmentIndex / 3;
        }
    }
}
