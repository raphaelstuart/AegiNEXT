using System.ComponentModel;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>连贯的颜色预览、HEX / RGBA 输入、模式切换与取色弹层；只提交颜色语义。</summary>
public sealed partial class ColorDraftInput : UserControl
{
    public static readonly StyledProperty<ColorDraft?> DraftProperty = AvaloniaProperty.Register<ColorDraftInput, ColorDraft?>(nameof(Draft));
    private readonly ColorView picker;
    private readonly ColorPreviewer preview;
    private readonly StackPanel draftRoot;
    private ColorDraft? model;
    private bool synchronizing;
    private int focusRevision;

    /// <summary>仅隔离内部编辑区的编译绑定上下文，外部 Draft 继续继承所属面板模型。</summary>
    public ColorDraftInput()
    {
        AvaloniaXamlLoader.Load(this);
        draftRoot = this.FindControl<StackPanel>("DraftRoot")!;
        picker = this.FindControl<ColorView>("Picker")!;
        preview = this.FindControl<ColorPreviewer>("ColorPreview")!;
        this.FindControl<Button>("ModeButton")!.Click += (_, _) =>
        {
            focusRevision++;
            _ = model?.TryToggleInputMode();
        };
        picker.ColorChanged += (_, args) =>
        {
            if (!synchronizing && model is not null && args.NewColor != SceneColorConversion.ToColor(model.Value))
            {
                model.SetValue(SceneColorConversion.FromColor(args.NewColor));
                _ = model.TryCommit();
            }
        };
        AddHandler(KeyDownEvent, InputKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, args) =>
        {
            if (args.Source is TextBox { Name: "ColorInput" } && model is { IsDirty: true } current)
            {
                var revision = ++focusRevision;
                Dispatcher.UIThread.Post(() =>
                {
                    if (revision == focusRevision && ReferenceEquals(current, model) && this.IsAttachedToVisualTree())
                    {
                        _ = current.TryCommit();
                    }
                }, DispatcherPriority.Background);
            }
        }, RoutingStrategies.Bubble);
        RefreshLanguage();
    }

    public ColorDraft? Draft
    {
        get => GetValue(DraftProperty);
        set => SetValue(DraftProperty, value);
    }

    /// <summary>按页面报告的字段定位本地输入，不触发验证或焦点回环。</summary>
    public bool TryFocusInvalidField(string? field = null)
    {
        field ??= model?.InvalidFieldKey;
        if (field is null)
        {
            return false;
        }

        var control = this.FindControl<TextBox>("ColorInput")!;
        control.BringIntoView();
        return control.Focus();
    }

    /// <summary>刷新共享输入文案，保留原始草稿和模式。</summary>
    public void RefreshLanguage()
    {
        var modeButton = this.FindControl<Button>("ModeButton")!;
        var pickerButton = this.FindControl<Button>("PickerButton")!;
        ToolTip.SetTip(modeButton, SettingsText.Get("ColorInputMode"));
        ToolTip.SetTip(pickerButton, SettingsText.Get("OpenColorPicker"));
        AutomationProperties.SetName(modeButton, SettingsText.Get("ColorInputMode"));
        AutomationProperties.SetName(pickerButton, SettingsText.Get("OpenColorPicker"));
        AutomationProperties.SetName(preview, SettingsText.Get("ColorPreview"));
        model?.RefreshLanguage();
    }

    /// <summary>模型替换时重接纯草稿订阅；回填不产生用户颜色提交。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DraftProperty)
        {
            if (model is not null)
            {
                model.PropertyChanged -= ModelChanged;
            }
            model = Draft;
            focusRevision++;
            if (draftRoot is not null)
            {
                draftRoot.DataContext = model;
            }
            if (model is not null)
            {
                model.PropertyChanged += ModelChanged;
            }
            if (picker is not null)
            {
                SynchronizePicker();
            }
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ColorDraft.Value) or nameof(ColorDraft.IsAlphaEnabled))
        {
            SynchronizePicker();
        }
    }

    private void SynchronizePicker()
    {
        if (model is null)
        {
            return;
        }
        synchronizing = true;
        try
        {
            picker.Color = SceneColorConversion.ToColor(model.Value);
            picker.IsAlphaEnabled = model.IsAlphaEnabled;
            picker.IsAlphaVisible = model.IsAlphaEnabled;
            preview.HsvColor = picker.Color.ToHsv();
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void InputKeyDown(object? sender, KeyEventArgs args)
    {
        if (model is null || args.Source is not Control source)
        {
            return;
        }
        var input = source.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault(control => control.Name == "ColorInput");
        if (input?.Name is not { } name)
        {
            return;
        }
        if (args.Key == Key.Escape)
        {
            focusRevision++;
            model.Restore(name);
            args.Handled = true;
        }
        else if (args.Key == Key.Enter)
        {
            _ = model.TryCommit();
            args.Handled = true;
        }
    }
}
