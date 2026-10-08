namespace AegiNext.Media.Analysis;

/// <summary>表示已完成音频缓存读取失败，当前会话已使缓存失效，可重新构建。</summary>
public sealed class AudioAnalysisCacheCorruptionException : IOException
{
    /// <summary>保存缓存校验失败的原因与原始异常。</summary>
    public AudioAnalysisCacheCorruptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
