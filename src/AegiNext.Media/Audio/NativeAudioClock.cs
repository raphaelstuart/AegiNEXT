using System.Runtime.InteropServices;
using System.Text;

namespace AegiNext.Media.Audio;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeAudioClock
{
    internal uint Size;
    internal uint Quality;
    internal long PlayedFrames;
    internal ulong HostTimestamp;
    internal ulong HostFrequency;
    internal ulong Epoch;
    internal int QueuedFrames;
    internal int SampleRate;
    internal int Channels;
    internal int Backend;
    internal fixed byte DeviceId[512];

    internal AudioOutputClockSnapshot ToSnapshot()
    {
        fixed (byte* pointer = DeviceId)
        {
            var bytes = new ReadOnlySpan<byte>(pointer, 512);
            var length = bytes.IndexOf((byte)0);
            if (length < 0 || Quality > 2 || PlayedFrames < 0 || HostFrequency == 0 || QueuedFrames is < 0 or > 12000 ||
                SampleRate <= 0 || Channels <= 0)
            {
                throw new InvalidDataException("原生音频播放时钟数据无效。");
            }
            return new(PlayedFrames, checked((long)HostTimestamp), checked((long)HostFrequency),
                System.Text.Encoding.UTF8.GetString(bytes[..length]), Backend switch { 1 => "CoreAudio", 2 => "WASAPI", 3 => "SDL", _ => "unknown" },
                checked((long)Epoch), (AudioClockQuality)Quality, QueuedFrames, SampleRate, Channels);
        }
    }
}
