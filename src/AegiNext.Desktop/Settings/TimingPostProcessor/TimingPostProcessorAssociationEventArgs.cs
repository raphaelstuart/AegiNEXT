using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>请求按稳定样式标识保存处理参数；空参数表示解除关联。</summary>
public sealed class TimingPostProcessorAssociationEventArgs(TimingPostProcessorOptions? options,
    ImmutableHashSet<Guid> styleIds) : EventArgs
{
    public TimingPostProcessorOptions? Options { get; } = options;
    public ImmutableHashSet<Guid> StyleIds { get; } = styleIds;
}
