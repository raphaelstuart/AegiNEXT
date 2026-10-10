using System.ComponentModel;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>组合参数输入并转发字段确认操作，不访问工程或播放器。</summary>
public sealed partial class TimingPostProcessorSettingsView : UserControl
{
    private TimingPostProcessorSettingsViewModel? observedModel;

    /// <summary>加载局部页面并连接数值草稿确认和恢复。</summary>
    public TimingPostProcessorSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => ChangeModel();
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

    private void ChangeModel()
    {
        CancelNumericDrags();
        if (observedModel is not null)
        {
            observedModel.PropertyChanged -= OnModelChanged;
            observedModel.StyleSelectionChanging -= OnStyleSelectionChanging;
        }
        observedModel = DataContext as TimingPostProcessorSettingsViewModel;
        if (observedModel is not null)
        {
            observedModel.PropertyChanged += OnModelChanged;
            observedModel.StyleSelectionChanging += OnStyleSelectionChanging;
        }
    }

    private void OnStyleSelectionChanging(object? sender, EventArgs args) => CancelNumericDrags();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TimingPostProcessorSettingsViewModel.SelectedStyle))
        {
            CancelNumericDrags();
        }
    }

    private void CancelNumericDrags()
    {
        foreach (var title in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            title.CancelDrag();
        }
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
        var title = this.FindControl<NumericDragLabel>(name + "Title")!;
        var focusRevision = 0L;
        TimingPostProcessorSettingsViewModel? dragModel = null;
        title.DragStarted += (_, _) =>
        {
            focusRevision++;
            dragModel = DataContext as TimingPostProcessorSettingsViewModel;
        };
        title.DragCompleted += (_, args) =>
        {
            focusRevision++;
            var frozenModel = dragModel;
            dragModel = null;
            if (args.Changed && input.IsEffectivelyVisible && frozenModel is { IsBusy: false } &&
                ReferenceEquals(DataContext, frozenModel))
            {
                frozenModel.Commit(field);
            }
        };
        title.DragCanceled += (_, _) =>
        {
            focusRevision++;
            dragModel = null;
        };
        DataContextChanged += (_, _) =>
        {
            focusRevision++;
            title.CancelDrag();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            {
                title.CancelDrag();
            }
        };
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is TimingPostProcessorSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
                focusRevision++;
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
            var revision = ++focusRevision;
            var currentModel = DataContext as TimingPostProcessorSettingsViewModel;
            var currentStyle = currentModel?.SelectedStyle;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision == focusRevision && !input.IsTitleDragging && !input.IsKeyboardFocusWithin && input.IsEffectivelyVisible &&
                    currentModel is { IsBusy: false } && ReferenceEquals(DataContext, currentModel) &&
                    ReferenceEquals(currentModel.SelectedStyle, currentStyle) && !HasComposition(input))
                {
                    currentModel.Commit(field);
                }
            }, DispatcherPriority.Background);
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
