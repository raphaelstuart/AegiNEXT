using AegiNext.Desktop.Editing;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Controls;

/// <summary>共享位置草稿的可复用视图；位置语义和持久化由所属面板承担。</summary>
public sealed partial class SubtitlePositionEditor : UserControl
{
    public static readonly StyledProperty<bool> ShowAutomaticPositionActionProperty =
        AvaloniaProperty.Register<SubtitlePositionEditor, bool>(nameof(ShowAutomaticPositionAction), true);

    /// <summary>决定宿主是否在位置编辑器内显示自动位置操作。</summary>
    public bool ShowAutomaticPositionAction
    {
        get => GetValue(ShowAutomaticPositionActionProperty);
        set => SetValue(ShowAutomaticPositionActionProperty, value);
    }

    private SubtitlePositionDraft? model;
    private readonly AnchorPresetPicker presets;
    /// <summary>创建数值编辑器，绑定外部纯位置草稿。</summary>
    public SubtitlePositionEditor()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        presets = this.FindControl<AnchorPresetPicker>("AnchorPresets")!;
        DataContextChanged += (_, _) => ChangeModel();
        presets.PresetSelected += (_, selection) =>
        {
            if (model?.SelectPreset(selection.Anchor, selection.SetPivot, selection.ResetOffset) == true)
            {
                PresetPositionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (model?.Validate() is { } field)
            {
                FocusInvalidField(field);
            }
        };
        this.FindControl<Button>("AutomaticPositionButton")!.Click += (_, _) =>
        {
            if (model is not null)
            {
                if (AutomaticPositionRequested is { } handler)
                {
                    handler(this, EventArgs.Empty);
                }
                else
                {
                    model.IsExplicit = false;
                    ExplicitPositionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        };
        var explicitPosition = this.FindControl<CheckBox>("ExplicitPositionCheck")!;
        explicitPosition.IsCheckedChanged += (_, _) =>
        {
            if (DataContext is SubtitlePositionDraft draft && draft.IsExplicit != (explicitPosition.IsChecked == true))
            {
                draft.IsExplicit = explicitPosition.IsChecked == true;
                ExplicitPositionChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>用户切换显式位置且纯草稿已经同步后，通知父面板提交；模型加载不触发此事件。</summary>
    public event EventHandler? ExplicitPositionChanged;

    /// <summary>请求所属面板恢复自动位置，工程动画的重置由所属协调服务决定。</summary>
    public event EventHandler? AutomaticPositionRequested;

    /// <summary>完整九宫格操作已原子更新纯草稿后通知所属面板提交。</summary>
    public event EventHandler? PresetPositionChanged;

    /// <summary>将验证焦点定位到该编辑器内部的数值字段。</summary>
    public bool FocusInvalidField(string fieldKey)
    {
        var control = this.FindControl<Control>(fieldKey);
        if (control is null)
        {
            return false;
        }

        control.BringIntoView();
        return control.Focus();
    }

    private void ChangeModel()
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
        }
        model = DataContext as SubtitlePositionDraft;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
        }
        RefreshSelection();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SubtitlePositionDraft.AnchorSelection) or nameof(SubtitlePositionDraft.PivotSelection))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        presets.SetSelection(model?.AnchorSelection, model?.PivotSelection);
    }
}
