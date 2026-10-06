using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>组合参数输入并转发字段确认操作，不访问工程或播放器。</summary>
public sealed partial class TimingPostProcessorSettingsView : UserControl
{
    /// <summary>加载局部页面并连接数值草稿确认和恢复。</summary>
    public TimingPostProcessorSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var styles = this.FindControl<ListBox>("TimingPostProcessorStylesList")!;
        styles.AddHandler(PointerPressedEvent, (_, args) => SelectStyle(args.Source), RoutingStrategies.Tunnel, true);
        styles.GotFocus += (_, args) => SelectStyle(args.Source);
        BindField("LeadInMillisecondsInput", TimingPostProcessorField.LEAD_IN);
        BindField("LeadOutMillisecondsInput", TimingPostProcessorField.LEAD_OUT);
        BindField("MaximumGapMillisecondsInput", TimingPostProcessorField.MAXIMUM_GAP);
        BindField("MaximumOverlapMillisecondsInput", TimingPostProcessorField.MAXIMUM_OVERLAP);
        BindField("StartBeforeMillisecondsInput", TimingPostProcessorField.START_BEFORE);
        BindField("StartAfterMillisecondsInput", TimingPostProcessorField.START_AFTER);
        BindField("EndBeforeMillisecondsInput", TimingPostProcessorField.END_BEFORE);
        BindField("EndAfterMillisecondsInput", TimingPostProcessorField.END_AFTER);
    }

    private void SelectStyle(object? source)
    {
        if (source is Control control && DataContext is TimingPostProcessorSettingsViewModel { IsBusy: false } model &&
            control.GetVisualAncestors().Prepend(control).OfType<CheckBox>().FirstOrDefault() is
                { DataContext: TimingStyleChoice choice })
        {
            model.SelectedStyle = choice;
        }
    }

    private void BindField(string name, TimingPostProcessorField field)
    {
        var input = this.FindControl<NumericDraftInput>(name)!;
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is TimingPostProcessorSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
                if (args.Key == Key.Escape)
                {
                    model.Restore(field);
                }
                else
                {
                    model.Commit(field);
                }

                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            if (IsEffectivelyVisible && DataContext is TimingPostProcessorSettingsViewModel { IsBusy: false } model &&
                !HasComposition(input))
            {
                model.Commit(field);
            }
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
