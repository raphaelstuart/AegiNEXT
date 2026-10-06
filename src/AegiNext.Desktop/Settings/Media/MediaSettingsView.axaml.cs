using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AegiNext.Desktop.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Media;

/// <summary>媒体设置页，只呈现偏好并发出语义切换请求。</summary>
public sealed partial class MediaSettingsView : UserControl
{
    /// <summary>加载绑定前隔离宿主页面上下文。</summary>
    public MediaSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<NumericDraftInput>("AudioExtraDelayInput")!;
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is MediaSettingsViewModel model && args.Key is Key.Enter or Key.Escape && !HasComposition(input))
            {
                if (args.Key == Key.Escape)
                {
                    model.RestoreAudioCalibration();
                }
                else
                {
                    model.CommitAudioCalibration();
                }
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            if (IsEffectivelyVisible && DataContext is MediaSettingsViewModel model && !HasComposition(input))
            {
                model.CommitAudioCalibration();
            }
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
