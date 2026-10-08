using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Settings.AudioAnalysis;

/// <summary>保存个人音频分析、执行预算及显示偏好。</summary>
public sealed record AudioAnalysisPreferences
{
    public bool AdvancedMode { get; init; }
    public AudioAnalysisExecutionOptions Execution { get; init; } = new();
    public AudioAnalysisRecipe Recipe { get; init; } = new();
    public AudioAnalysisDisplayOptions Display { get; init; } = new();

    /// <summary>验证持久偏好，不按当前机器核心数拒绝迁移来的线程设置。</summary>
    public void Validate()
    {
        if (Execution is null || Recipe is null || Display is null)
        {
            throw new InvalidDataException("音频分析偏好缺少参数。");
        }
        Execution.Validate();
        Recipe.Validate();
        Display.Validate();
    }
}
