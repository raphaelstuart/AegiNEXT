using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using System.Text.Json.Serialization;

namespace AegiNext.Desktop.Settings.Media;

/// <summary>仅用于本机输出链路的额外延迟，正值表示声音比系统报告的位置更晚出声。</summary>
public sealed record AudioDeviceCalibration(string DeviceId, string Backend, int SampleRate, int Channels,
    int ExtraDelayMilliseconds)
{
    /// <summary>按设备、后端和实际输出格式匹配校准，禁止复用另一条输出链路的值。</summary>
    public bool Matches(AudioOutputClockSnapshot clock)
    {
        return DeviceId == clock.DeviceId && Backend == clock.Backend && SampleRate == clock.SampleRate && Channels == clock.Channels;
    }

    [JsonIgnore]
    public MediaTime Delay => new(ExtraDelayMilliseconds, 1000);

    /// <summary>验证可持久化的设备身份、输出格式和有界毫秒校准。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DeviceId) || DeviceId.Length > 512 || string.IsNullOrWhiteSpace(Backend) ||
            Backend.Length > 64 || SampleRate is < 8000 or > 384000 || Channels is < 1 or > 64 ||
            ExtraDelayMilliseconds is < -1000 or > 1000)
        {
            throw new InvalidDataException("音频设备校准无效。");
        }
    }
}
