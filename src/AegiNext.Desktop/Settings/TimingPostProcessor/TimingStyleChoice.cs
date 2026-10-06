using CommunityToolkit.Mvvm.ComponentModel;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>全局样式库中可关联处理参数的样式投影，使用稳定标识保留选择。</summary>
public sealed class TimingStyleChoice(Guid id, string name, TimingPostProcessorOptions? options,
    bool isSelected = false) : ObservableObject
{
    private bool selected = isSelected;

    public string Name { get; } = name;
    public Guid Id { get; } = id;
    public TimingPostProcessorOptions? Options { get; } = options;
    public bool IsAssociated => Options is not null;

    public bool IsSelected
    {
        get => selected;
        set => SetProperty(ref selected, value);
    }
}
