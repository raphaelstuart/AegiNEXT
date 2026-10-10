namespace AegiNext.Rendering.Tests.Reference;

/// <summary>只有显式配置独立 libass 库时执行的参数化外部渲染验证。</summary>
public sealed class LibassReferenceTheoryAttribute : TheoryAttribute
{
    /// <summary>未配置库时明确跳过；已配置但无法加载时让测试失败。</summary>
    public LibassReferenceTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEGINEXT_LIBASS_REFERENCE_PATH")))
        {
            Skip = "Configure AEGINEXT_LIBASS_REFERENCE_PATH to run independent libass reference rendering.";
        }
    }
}
