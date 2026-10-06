using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Settings.TimingPostProcessor;

/// <summary>保存最近编辑的参数；样式关联由样式库独立持久化。</summary>
public sealed record TimingPostProcessorPreferences
{
    public TimingPostProcessorOptions Options { get; init; } = new();

    /// <summary>验证完整参数快照。</summary>
    public void Validate()
    {
        try
        {
            ArgumentNullException.ThrowIfNull(Options);
            Options.Validate();
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("Timing post-processor preferences are invalid.", error);
        }
    }
}
