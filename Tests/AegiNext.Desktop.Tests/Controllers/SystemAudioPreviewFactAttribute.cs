namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>显式启用真实视频解码和系统音频设备的静音联动验证。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SystemAudioPreviewFactAttribute : FactAttribute
{
    /// <summary>需要原生解码、音频集成和系统输出三个测试开关。</summary>
    public SystemAudioPreviewFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AEGINEXT_RUN_DECODER_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("AEGINEXT_RUN_AUDIO_TESTS") != "1" ||
            Environment.GetEnvironmentVariable("AEGINEXT_RUN_SYSTEM_AUDIO_TESTS") != "1")
        {
            Skip = "Requires native decoder/audio libraries, FFmpeg, and AEGINEXT_RUN_DECODER_TESTS=1, AEGINEXT_RUN_AUDIO_TESTS=1, AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1.";
        }
    }
}
