namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>显式启用用户原片的真实预览链路验证。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OriginalVideoCompatibilityFactAttribute : FactAttribute
{
    /// <summary>原生测试和原片路径须同时显式指定；启用后无效路径必须失败。</summary>
    public OriginalVideoCompatibilityFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AEGINEXT_RUN_DECODER_TESTS") != "1" ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEGINEXT_COMPATIBILITY_MEDIA_PATH")))
        {
            Skip = "Requires AEGINEXT_RUN_DECODER_TESTS=1 and AEGINEXT_COMPATIBILITY_MEDIA_PATH.";
        }
    }
}
