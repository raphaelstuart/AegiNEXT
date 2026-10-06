#include "audio_output.h"
#include "audio_output_timeline.h"
#include <AudioToolbox/AudioToolbox.h>
#include <CoreAudio/CoreAudio.h>
#include <CoreFoundation/CoreFoundation.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
void check_audio(OSStatus status, const char *operation)
{
    if (status != noErr)
    {
        throw std::runtime_error(std::string(operation) + " (" + std::to_string(status) + ")");
    }
}

class CoreAudioOutput final : public AudioOutput
{
public:
    CoreAudioOutput()
    {
        try
        {
            AudioObjectPropertyAddress address{kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
            UInt32 size = sizeof(device);
            check_audio(AudioObjectGetPropertyData(kAudioObjectSystemObject, &address, 0, nullptr, &size, &device), "Read default output device");
            if (device == kAudioObjectUnknown)
            {
                throw std::runtime_error("No CoreAudio output device");
            }
            address.mSelector = kAudioDevicePropertyDeviceUID;
            CFStringRef uid = nullptr;
            size = sizeof(uid);
            check_audio(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, &uid), "Read CoreAudio device UID");
            const auto encoded = uid && CFStringGetCString(uid, device_id.data(), device_id.size(), kCFStringEncodingUTF8);
            if (uid) { CFRelease(uid); }
            if (!encoded) { throw std::runtime_error("CoreAudio device UID is invalid"); }
            address.mSelector = kAudioDevicePropertyNominalSampleRate;
            Float64 rate = 0;
            size = sizeof(rate);
            check_audio(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, &rate), "Read CoreAudio output rate");
            output_rate = static_cast<int>(std::llround(rate));
            address = {kAudioDevicePropertyStreamConfiguration, kAudioObjectPropertyScopeOutput, kAudioObjectPropertyElementMain};
            check_audio(AudioObjectGetPropertyDataSize(device, &address, 0, nullptr, &size), "Read CoreAudio channel layout size");
            std::vector<uint8_t> layout(size);
            check_audio(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, layout.data()), "Read CoreAudio channel layout");
            const auto *buffers = reinterpret_cast<const AudioBufferList *>(layout.data());
            for (UInt32 index = 0; index < buffers->mNumberBuffers; ++index) { output_channels += buffers->mBuffers[index].mNumberChannels; }
            if (output_rate <= 0 || output_channels <= 0) { throw std::runtime_error("CoreAudio output format is invalid"); }

            create_queue();
            addresses = {{{kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
                {kAudioDevicePropertyDeviceIsAlive, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
                {kAudioDevicePropertyNominalSampleRate, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain},
                {kAudioDevicePropertyStreamConfiguration, kAudioObjectPropertyScopeOutput, kAudioObjectPropertyElementMain}}};
            for (size_t index = 0; index < addresses.size(); ++index)
            {
                check_audio(AudioObjectAddPropertyListener(index == 0 ? kAudioObjectSystemObject : device, &addresses[index], changed, this), "Observe CoreAudio route");
                ++listeners;
            }
        }
        catch (...)
        {
            release();
            throw;
        }
    }

    ~CoreAudioOutput() override { release(); }

    void write(const float *samples, int frames) override
    {
        std::scoped_lock operation(api_gate);
        std::scoped_lock state(gate);
        require_valid();
        update_clock();
        if (submitted - played + frames > CAPACITY) { throw std::runtime_error("CoreAudio PCM queue capacity exceeded"); }
        for (int index = 0; index < frames; ++index)
        {
            const auto destination = static_cast<size_t>((ring_start + ring_count + index) % CAPACITY) * 2;
            pcm[destination] = samples[index * 2];
            pcm[destination + 1] = samples[index * 2 + 1];
        }
        ring_count += frames;
        submitted += frames;
    }

    void pause(bool value) override
    {
        std::scoped_lock operation(api_gate);
        if (value)
        {
            if (paused.exchange(true)) { return; }
            check_audio(AudioQueuePause(queue), "Pause CoreAudio output");
            std::scoped_lock state(gate);
            update_clock(true);
        }
        else
        {
            require_valid();
            if (!paused.exchange(false)) { return; }
            {
                std::scoped_lock state(gate);
                for (size_t index = 0; index < output_buffers.size(); ++index)
                {
                    if (!buffer_queued[index]) { enqueue(index); }
                }
            }
            check_audio(AudioQueueStart(queue, nullptr), "Start CoreAudio output");
        }
    }

    void clear() override
    {
        std::scoped_lock operation(api_gate);
        if (!paused.load()) { throw std::runtime_error("CoreAudio Clear requires paused output"); }
        require_valid();
        dispose_queue();
        try
        {
            create_queue();
        }
        catch (...)
        {
            invalidated.store(true);
            throw;
        }
        std::scoped_lock state(gate);
        buffer_queued.fill(false);
        timeline.reset();
        ring_start = ring_count = 0;
        submitted = rendered = played = 0;
        ++epoch;
    }

    void gain(float value) override
    {
        std::scoped_lock operation(api_gate);
        check_audio(AudioQueueSetParameter(queue, kAudioQueueParam_Volume, value), "Set CoreAudio output gain");
    }

    int latency() const override { return 0; }

    an_audio_clock_snapshot snapshot() override
    {
        std::scoped_lock operation(api_gate);
        std::scoped_lock state(gate);
        if (!invalidated.load()) { update_clock(); }
        an_audio_clock_snapshot result{};
        result.size = sizeof(result);
        result.quality = invalidated.load() ? 0 : 2;
        result.played_frames = played;
        result.host_timestamp = clock_host_time;
        result.host_frequency = static_cast<uint64_t>(AudioGetHostClockFrequency());
        result.epoch = epoch;
        result.queued_frames = static_cast<int>(std::clamp<int64_t>(submitted - played, 0, CAPACITY));
        result.sample_rate = output_rate;
        result.channels = output_channels;
        result.backend = 1;
        std::memcpy(result.device_id, device_id.data(), device_id.size());
        return result;
    }

private:
    static constexpr int CAPACITY = 12000;
    static constexpr int BUFFER_FRAMES = 1024;
    AudioQueueRef queue = nullptr;
    AudioQueueTimelineRef queue_timeline = nullptr;
    AudioDeviceID device = kAudioObjectUnknown;
    std::array<AudioObjectPropertyAddress, 4> addresses{};
    size_t listeners = 0;
    std::array<char, 512> device_id{};
    std::array<AudioQueueBufferRef, 3> output_buffers{};
    std::array<bool, 3> buffer_queued{};
    std::array<float, CAPACITY * 2> pcm{};
    AudioOutputTimeline timeline;
    std::mutex api_gate;
    std::mutex gate;
    std::atomic_bool paused{true};
    std::atomic_bool invalidated{false};
    int output_rate = 0;
    int output_channels = 0;
    int ring_start = 0;
    int ring_count = 0;
    int64_t submitted = 0;
    int64_t rendered = 0;
    int64_t played = 0;
    uint64_t epoch = 0;
    uint64_t clock_host_time = AudioGetCurrentHostTime();

    void create_queue()
    {
    AudioStreamBasicDescription format{};
    format.mSampleRate = 48000;
    format.mFormatID = kAudioFormatLinearPCM;
    format.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
    format.mBytesPerPacket = format.mBytesPerFrame = 8;
    format.mFramesPerPacket = 1;
    format.mChannelsPerFrame = 2;
    format.mBitsPerChannel = 32;
    check_audio(AudioQueueNewOutput(&format, callback, this, nullptr, nullptr, 0, &queue), "Create CoreAudio output queue");
    auto device_uid = CFStringCreateWithCString(nullptr, device_id.data(), kCFStringEncodingUTF8);
    const auto bound = AudioQueueSetProperty(queue, kAudioQueueProperty_CurrentDevice, &device_uid, sizeof(device_uid));
    CFRelease(device_uid);
    check_audio(bound, "Bind CoreAudio output device");
    check_audio(AudioQueueCreateTimeline(queue, &queue_timeline), "Create CoreAudio timeline");
    for (auto &buffer : output_buffers)
    {
        check_audio(AudioQueueAllocateBuffer(queue, BUFFER_FRAMES * 8, &buffer), "Allocate CoreAudio output buffer");
    }
    }

    void dispose_queue()
    {
        if (queue)
        {
            if (queue_timeline)
            {
                AudioQueueDisposeTimeline(queue, queue_timeline);
                queue_timeline = nullptr;
            }
            AudioQueueDispose(queue, true);
            queue = nullptr;
        }
    }

    void require_valid() const
    {
        if (invalidated.load()) { throw std::runtime_error("CoreAudio output route or timeline changed; reopen the device"); }
    }

    void update_clock(bool force = false)
    {
        if (paused.load() && !force) { return; }
        AudioTimeStamp time{};
        Boolean discontinuity = false;
        const auto status = AudioQueueGetCurrentTime(queue, queue_timeline, &time, &discontinuity);
        if (status != noErr || discontinuity)
        {
            invalidated.store(true);
            return;
        }
        if ((time.mFlags & kAudioTimeStampSampleTimeValid) != 0)
        {
            played = timeline.read(static_cast<int64_t>(std::floor(time.mSampleTime)));
            clock_host_time = (time.mFlags & kAudioTimeStampHostTimeValid) != 0 ? time.mHostTime : AudioGetCurrentHostTime();
        }
    }

    void enqueue(size_t index)
    {
        auto buffer = output_buffers[index];
        auto *destination = static_cast<float *>(buffer->mAudioData);
        const auto frames = std::min(ring_count, BUFFER_FRAMES);
        std::fill_n(destination, BUFFER_FRAMES * 2, 0.0F);
        for (int frame = 0; frame < frames; ++frame)
        {
            const auto source = static_cast<size_t>((ring_start + frame) % CAPACITY) * 2;
            destination[frame * 2] = pcm[source];
            destination[frame * 2 + 1] = pcm[source + 1];
        }
        buffer->mAudioDataByteSize = BUFFER_FRAMES * 8;
        AudioTimeStamp start{};
        check_audio(AudioQueueEnqueueBufferWithParameters(queue, buffer, 0, nullptr, 0, 0, 0, nullptr, nullptr, &start), "Enqueue CoreAudio PCM");
        if ((start.mFlags & kAudioTimeStampSampleTimeValid) == 0)
        {
            throw std::runtime_error("CoreAudio did not report PCM presentation time");
        }
        timeline.append(static_cast<int64_t>(std::llround(start.mSampleTime)), rendered, frames, BUFFER_FRAMES);
        ring_start = (ring_start + frames) % CAPACITY;
        ring_count -= frames;
        rendered += frames;
        buffer_queued[index] = true;
    }

    static void callback(void *context, AudioQueueRef, AudioQueueBufferRef buffer)
    {
        auto &output = *static_cast<CoreAudioOutput *>(context);
        std::scoped_lock state(output.gate);
        const auto iterator = std::find(output.output_buffers.begin(), output.output_buffers.end(), buffer);
        if (iterator == output.output_buffers.end()) { output.invalidated.store(true); return; }
        const auto index = static_cast<size_t>(iterator - output.output_buffers.begin());
        output.buffer_queued[index] = false;
        if (output.paused.load() || output.invalidated.load()) { return; }
        try
        {
            output.update_clock();
            if (!output.invalidated.load()) { output.enqueue(index); }
        }
        catch (...) { output.invalidated.store(true); }
    }

    static OSStatus changed(AudioObjectID, UInt32, const AudioObjectPropertyAddress *, void *context)
    {
        static_cast<CoreAudioOutput *>(context)->invalidated.store(true);
        return noErr;
    }

    void release()
    {
        paused.store(true);
        for (size_t index = 0; index < listeners; ++index)
        {
            AudioObjectRemovePropertyListener(index == 0 ? kAudioObjectSystemObject : device, &addresses[index], changed, this);
        }
        listeners = 0;
        dispose_queue();
    }
};
}

std::unique_ptr<AudioOutput> create_system_audio_output()
{
    return std::make_unique<CoreAudioOutput>();
}
