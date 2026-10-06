namespace AegiNext.Media.Tests.Probing;

/// <summary>
/// 只在显式提供工具路径时运行真实媒体集成；普通测试会显示明确的跳过原因。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MediaToolsTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// 读取集成测试的显式工具配置。
    /// </summary>
    public MediaToolsTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")))
        {
            Skip = "真实媒体测试需显式设置 AEGINEXT_FFPROBE_PATH 和 AEGINEXT_FFMPEG_PATH；普通构建不安装媒体工具。";
        }
    }
}
