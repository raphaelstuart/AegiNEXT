namespace AegiNext.Media.Analysis;

/// <summary>为一个音频分析会话组合不可变分析参数与可更新执行参数。</summary>
public sealed record AudioAnalysisOptions
{
    public AudioAnalysisRecipe Recipe { get; init; } = new();
    public AudioAnalysisExecutionOptions Execution { get; init; } = new();

    /// <summary>验证分析配方及执行配置。</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Recipe);
        ArgumentNullException.ThrowIfNull(Execution);
        Recipe.Validate();
        Execution.Validate();
    }
}
