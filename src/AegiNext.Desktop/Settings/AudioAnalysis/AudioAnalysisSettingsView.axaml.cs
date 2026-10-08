using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.AudioAnalysis;

/// <summary>呈现分析参数，连接字段确认、恢复及输入法组合保护。</summary>
public sealed partial class AudioAnalysisSettingsView : UserControl
{
    /// <summary>加载独立页面并连接本地数字草稿。</summary>
    public AudioAnalysisSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        foreach (var (name, field) in new[]
                 {
                     ("MaximumWorkersInput", AudioAnalysisSettingsField.MAXIMUM_WORKERS),
                     ("MemoryBudgetInput", AudioAnalysisSettingsField.MEMORY_BUDGET),
                     ("WaveformGainInput", AudioAnalysisSettingsField.WAVEFORM_GAIN),
                     ("SpectrumBrightnessInput", AudioAnalysisSettingsField.SPECTRUM_BRIGHTNESS),
                     ("SpectrumContrastInput", AudioAnalysisSettingsField.SPECTRUM_CONTRAST),
                     ("MinimumFrequencyInput", AudioAnalysisSettingsField.MINIMUM_FREQUENCY),
                     ("MaximumFrequencyInput", AudioAnalysisSettingsField.MAXIMUM_FREQUENCY),
                     ("MinimumDecibelsInput", AudioAnalysisSettingsField.MINIMUM_DECIBELS),
                     ("MaximumDecibelsInput", AudioAnalysisSettingsField.MAXIMUM_DECIBELS)
                 })
        {
            var input = this.FindControl<NumericDraftInput>(name)!;
            input.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (DataContext is AudioAnalysisSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
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
                if (input.IsEffectivelyVisible && DataContext is AudioAnalysisSettingsViewModel model && !HasComposition(input))
                {
                    model.Commit(field);
                }
            };
        }
    }

    private static bool HasComposition(Control input)
    {
        return input.GetVisualDescendants().OfType<TextPresenter>()
            .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
    }
}
