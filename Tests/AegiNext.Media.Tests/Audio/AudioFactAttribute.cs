namespace AegiNext.Media.Tests.Audio;

/// <summary>仅在显式启用时运行需要原生音频库的集成测试。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AudioFactAttribute : FactAttribute
{
    /// <summary>启用后缺失的原生库或媒体工具必须作为失败报告。</summary>
    public AudioFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AEGINEXT_RUN_AUDIO_TESTS") != "1")
        {
            Skip = "音频集成测试需设置 AEGINEXT_RUN_AUDIO_TESTS=1，并准备原生音频库与 AEGINEXT_FFMPEG_PATH。";
        }
    }
}
