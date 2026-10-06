namespace AegiNext.Desktop.Tests.Rendering;

/// <summary>显式启用真实解码、转换和场景预览验证。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PreviewQualityNativeFactAttribute : FactAttribute
{
    /// <summary>使用既有原生解码测试开关；启用后缺少工具必须失败。</summary>
    public PreviewQualityNativeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AEGINEXT_RUN_DECODER_TESTS") != "1")
        {
            Skip = "Requires AEGINEXT_RUN_DECODER_TESTS=1, native decoder and AEGINEXT_FFMPEG_PATH.";
        }
    }
}
