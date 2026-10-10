namespace AegiNext.Rendering.Tests.Reference;

/// <summary>只有显式配置独立 libass 库时执行的外部渲染验证。</summary>
public sealed class LibassReferenceFactAttribute : FactAttribute
{
    /// <summary>未配置库时明确跳过；已配置但无法加载时让测试失败。</summary>
    public LibassReferenceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEGINEXT_LIBASS_REFERENCE_PATH")))
        {
            Skip = "Configure AEGINEXT_LIBASS_REFERENCE_PATH to run independent libass reference rendering.";
        }
    }
}
