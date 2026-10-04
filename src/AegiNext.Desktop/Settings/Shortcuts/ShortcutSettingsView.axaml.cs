using System.ComponentModel;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Shortcuts;

/// <summary>按键录入的局部适配，不持有工程或全局输入路由。</summary>
public sealed partial class ShortcutSettingsView : UserControl
{
    private ShortcutSettingsViewModel? model;

    /// <summary>先隔离父级上下文，再加载编译绑定并接线局部键盘输入。</summary>
    public ShortcutSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => ChangeModel();
        AddHandler(KeyDownEvent, RecordShortcut, RoutingStrategies.Tunnel);
    }

    private void ChangeModel()
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
        }

        model = DataContext as ShortcutSettingsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            UpdateRecordLabel();
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutSettingsViewModel.RecordLabel))
        {
            UpdateRecordLabel();
        }
        else if (e.PropertyName == nameof(ShortcutSettingsViewModel.IsRecording) && model?.IsRecording == true)
        {
            this.FindControl<TextBox>("GestureInput")!.Focus();
        }
    }

    private void UpdateRecordLabel()
    {
        SettingsViewLocalization.SetButtonContent(this.FindControl<Button>("RecordShortcutButton")!, model!.RecordLabel, "Record");
    }

    private void FocusGesture(object? sender, RoutedEventArgs e)
    {
        if (model?.IsRecording == true)
        {
            this.FindControl<TextBox>("GestureInput")!.Focus();
        }
    }

    private void RecordShortcut(object? sender, KeyEventArgs e)
    {
        if (model?.IsRecording != true)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            model.IsRecording = false;
            return;
        }

        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        try
        {
            model.CaptureGesture(ShortcutConfiguration.FormatGesture(e.Key, e.KeyModifiers));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or FormatException)
        {
            model.RejectGesture();
        }
    }
}
