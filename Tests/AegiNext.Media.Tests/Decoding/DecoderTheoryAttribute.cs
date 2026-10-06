namespace AegiNext.Media.Tests.Decoding;

/// <summary>
/// 仅在显式启用时运行依赖原生解码库的数据驱动测试。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DecoderTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// 读取原生解码测试开关；启用后的环境缺失由测试报告失败。
    /// </summary>
    public DecoderTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("AEGINEXT_RUN_DECODER_TESTS") != "1")
        {
            Skip = "原生解码测试需显式设置 AEGINEXT_RUN_DECODER_TESTS=1，并准备解码库与媒体工具。";
        }
    }
}
