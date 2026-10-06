using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Preview;

/// <summary>呈现预览设置，并将确认与恢复操作交给页面草稿模型。</summary>
public sealed partial class PreviewSettingsView : UserControl
{
    /// <summary>加载试听长度输入并连接本地键盘和焦点操作。</summary>
    public PreviewSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<NumericDraftInput>("SubtitleAuditionMillisecondsInput")!;
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is PreviewSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
                if (args.Key == Key.Escape)
                {
                    model.RestoreSubtitleAuditionMilliseconds();
                }
                else
                {
                    model.CommitSubtitleAuditionMilliseconds();
                }

                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            if (IsEffectivelyVisible && DataContext is PreviewSettingsViewModel model && !HasComposition(input))
            {
                model.CommitSubtitleAuditionMilliseconds();
            }
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
