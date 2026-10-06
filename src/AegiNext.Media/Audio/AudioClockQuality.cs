namespace AegiNext.Media.Audio;

/// <summary>输出时钟的观测依据；设备失效时不能继续把最后读数作为可信播放位置。</summary>
public enum AudioClockQuality
{
    UNAVAILABLE,
    ESTIMATED,
    SYSTEM
}
