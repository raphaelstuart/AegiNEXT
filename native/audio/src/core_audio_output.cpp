#include "audio_output.h"
#include "audio_output_timeline.h"
#include "core_audio_clock_continuity.h"
#include <AudioToolbox/AudioToolbox.h>
#include <CoreAudio/CoreAudio.h>
#include <CoreFoundation/CoreFoundation.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
enum class CoreAudioFailure
{
    NONE,
    DEFAULT_ROUTE,
    DEVICE_OFFLINE,
    DEVICE_IDENTITY,
    OUTPUT_RATE,
    OUTPUT_CHANNELS,
    ROUTE_QUERY,
    CLOCK_STATUS,
    CLOCK_TIMESTAMP,
    CLOCK_REGRESSION,
    UNKNOWN_BUFFER,
    CALLBACK,
    QUEUE_CREATION,
    QUEUE_PAUSE
};

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
        require_valid();
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
            const auto status = AudioQueuePause(queue);
            std::scoped_lock state(gate);
            update_clock(true);
            if (status != noErr && !invalidated.load())
            {
                invalidate(CoreAudioFailure::QUEUE_PAUSE, status);
                require_valid();
            }
        }
        else
        {
            {
                std::scoped_lock state(gate);
                require_valid();
                if (!paused.load()) { return; }
                clock_continuity.begin_run();
                clock_start_host_time = AudioGetCurrentHostTime();
                paused.store(false);
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
            invalidate(CoreAudioFailure::QUEUE_CREATION);
            throw;
        }
        std::scoped_lock state(gate);
        buffer_queued.fill(false);
        timeline.reset();
        clock_continuity.reset();
        ring_start = ring_count = 0;
        submitted = rendered = played = 0;
        ++epoch;
    }

    void gain(float value) override
    {
        std::scoped_lock operation(api_gate);
        check_audio(AudioQueueSetParameter(queue, kAudioQueueParam_Volume, value), "Set CoreAudio output gain");
        output_gain = value;
    }

    float gain() override
    {
        std::scoped_lock operation(api_gate);
        AudioQueueParameterValue value = 0;
        check_audio(AudioQueueGetParameter(queue, kAudioQueueParam_Volume, &value), "Read CoreAudio output gain");
        return value;
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
    CoreAudioClockContinuity clock_continuity;
    std::mutex api_gate;
    std::mutex gate;
    std::atomic_bool paused{true};
    std::atomic_bool invalidated{false};
    std::atomic_bool route_notification{false};
    std::atomic<CoreAudioFailure> failure{CoreAudioFailure::NONE};
    std::atomic<OSStatus> clock_status{noErr};
    int output_rate = 0;
    int output_channels = 0;
    float output_gain = 1.0F;
    int ring_start = 0;
    int ring_count = 0;
    int64_t submitted = 0;
    int64_t rendered = 0;
    int64_t played = 0;
    uint64_t epoch = 0;
    uint64_t clock_host_time = AudioGetCurrentHostTime();
    uint64_t clock_start_host_time = 0;

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
        check_audio(AudioQueueSetParameter(queue, kAudioQueueParam_Volume, output_gain), "Restore CoreAudio output gain");
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
            {
                std::scoped_lock state(gate);
                queue_timeline = nullptr;
            }
            AudioQueueDispose(queue, true);
            queue = nullptr;
        }
    }

    void invalidate(CoreAudioFailure reason, OSStatus status = noErr)
    {
        if (!invalidated.load())
        {
            failure.store(reason);
            clock_status.store(status);
            invalidated.store(true);
        }
    }

    bool route_query(OSStatus status)
    {
        if (status != noErr)
        {
            invalidate(CoreAudioFailure::ROUTE_QUERY, status);
            return false;
        }
        return true;
    }

    void confirm_route()
    {
        try
        {
            AudioObjectPropertyAddress address{kAudioHardwarePropertyDefaultOutputDevice, kAudioObjectPropertyScopeGlobal, kAudioObjectPropertyElementMain};
            AudioDeviceID current = kAudioObjectUnknown;
            UInt32 size = sizeof(current);
            if (!route_query(AudioObjectGetPropertyData(kAudioObjectSystemObject, &address, 0, nullptr, &size, &current))) { return; }
            if (current != device) { invalidate(CoreAudioFailure::DEFAULT_ROUTE); return; }
            address.mSelector = kAudioDevicePropertyDeviceIsAlive;
            UInt32 alive = 0;
            size = sizeof(alive);
            if (!route_query(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, &alive))) { return; }
            if (!alive) { invalidate(CoreAudioFailure::DEVICE_OFFLINE); return; }
            address.mSelector = kAudioDevicePropertyDeviceUID;
            CFStringRef uid = nullptr;
            size = sizeof(uid);
            if (!route_query(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, &uid))) { return; }
            std::array<char, 512> current_id{};
            const auto encoded = uid && CFStringGetCString(uid, current_id.data(), current_id.size(), kCFStringEncodingUTF8);
            if (uid) { CFRelease(uid); }
            if (!encoded || current_id != device_id) { invalidate(CoreAudioFailure::DEVICE_IDENTITY); return; }
            address.mSelector = kAudioDevicePropertyNominalSampleRate;
            Float64 rate = 0;
            size = sizeof(rate);
            if (!route_query(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, &rate))) { return; }
            if (!std::isfinite(rate) || std::llround(rate) != output_rate) { invalidate(CoreAudioFailure::OUTPUT_RATE); return; }
            address = {kAudioDevicePropertyStreamConfiguration, kAudioObjectPropertyScopeOutput, kAudioObjectPropertyElementMain};
            if (!route_query(AudioObjectGetPropertyDataSize(device, &address, 0, nullptr, &size))) { return; }
            std::vector<uint8_t> layout(size);
            if (!route_query(AudioObjectGetPropertyData(device, &address, 0, nullptr, &size, layout.data()))) { return; }
            const auto *buffers = reinterpret_cast<const AudioBufferList *>(layout.data());
            int channels = 0;
            for (UInt32 index = 0; index < buffers->mNumberBuffers; ++index)
            {
                channels += buffers->mBuffers[index].mNumberChannels;
            }
            if (channels != output_channels) { invalidate(CoreAudioFailure::OUTPUT_CHANNELS); }
        }
        catch (...)
        {
            invalidate(CoreAudioFailure::ROUTE_QUERY);
        }
    }

    void require_valid()
    {
        if (route_notification.exchange(false)) { confirm_route(); }
        if (!invalidated.load()) { return; }
        const char *reason = "output failed";
        switch (failure.load())
        {
            case CoreAudioFailure::DEFAULT_ROUTE: reason = "default output route changed"; break;
            case CoreAudioFailure::DEVICE_OFFLINE: reason = "output device is no longer alive"; break;
            case CoreAudioFailure::DEVICE_IDENTITY: reason = "output device UID changed"; break;
            case CoreAudioFailure::OUTPUT_RATE: reason = "output device sample rate changed"; break;
            case CoreAudioFailure::OUTPUT_CHANNELS: reason = "output device channel layout changed"; break;
            case CoreAudioFailure::ROUTE_QUERY: reason = "output device property query failed"; break;
            case CoreAudioFailure::CLOCK_STATUS: reason = "queue clock query failed"; break;
            case CoreAudioFailure::CLOCK_TIMESTAMP: reason = "queue clock has no valid sample timestamp"; break;
            case CoreAudioFailure::CLOCK_REGRESSION: reason = "queue sample timestamp moved backwards"; break;
            case CoreAudioFailure::UNKNOWN_BUFFER: reason = "queue returned an unknown buffer"; break;
            case CoreAudioFailure::CALLBACK: reason = "queue PCM callback failed"; break;
            case CoreAudioFailure::QUEUE_CREATION: reason = "queue recreation failed"; break;
            case CoreAudioFailure::QUEUE_PAUSE: reason = "queue pause failed"; break;
            case CoreAudioFailure::NONE: break;
        }
        throw std::runtime_error(std::string("CoreAudio ") + reason + " (" + std::to_string(clock_status.load()) + "); reopen the device");
    }

    void update_clock(bool force = false)
    {
        if (route_notification.exchange(false)) { confirm_route(); }
        if (invalidated.load()) { return; }
        if (paused.load() && !force) { return; }
        AudioTimeStamp time{};
        Boolean discontinuity = false;
        const auto status = AudioQueueGetCurrentTime(queue, queue_timeline, &time, &discontinuity);
        const auto within_startup_grace = AudioGetCurrentHostTime() - clock_start_host_time < AudioConvertNanosToHostTime(1'000'000'000);
        if (status != noErr)
        {
            if (clock_continuity.can_wait_for_timestamp(status == kAudioQueueErr_InvalidRunState, paused.load()) &&
                (paused.load() || within_startup_grace))
            {
                return;
            }
            invalidate(CoreAudioFailure::CLOCK_STATUS, status);
            return;
        }
        if (discontinuity)
        {
            confirm_route();
            if (invalidated.load()) { return; }
        }
        if ((time.mFlags & kAudioTimeStampSampleTimeValid) == 0 || !std::isfinite(time.mSampleTime) ||
            time.mSampleTime < static_cast<double>(std::numeric_limits<int64_t>::min()) ||
            time.mSampleTime >= static_cast<double>(std::numeric_limits<int64_t>::max()))
        {
            invalidate(CoreAudioFailure::CLOCK_TIMESTAMP);
            return;
        }
        const auto sample_frame = static_cast<int64_t>(std::floor(time.mSampleTime));
        const auto observation = clock_continuity.observe(sample_frame, discontinuity, true, within_startup_grace);
        if (observation == CoreAudioClockObservation::WAITING)
        {
            return;
        }
        if (observation == CoreAudioClockObservation::INVALID)
        {
            invalidate(CoreAudioFailure::CLOCK_REGRESSION);
            return;
        }
        played = timeline.read(sample_frame);
        clock_host_time = (time.mFlags & kAudioTimeStampHostTimeValid) != 0 ? time.mHostTime : AudioGetCurrentHostTime();
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
        if (iterator == output.output_buffers.end()) { output.invalidate(CoreAudioFailure::UNKNOWN_BUFFER); return; }
        const auto index = static_cast<size_t>(iterator - output.output_buffers.begin());
        output.buffer_queued[index] = false;
        if (output.paused.load() || output.invalidated.load()) { return; }
        try
        {
            output.update_clock();
            if (!output.invalidated.load()) { output.enqueue(index); }
        }
        catch (...) { output.invalidate(CoreAudioFailure::CALLBACK); }
    }

    static OSStatus changed(AudioObjectID, UInt32, const AudioObjectPropertyAddress *, void *context)
    {
        static_cast<CoreAudioOutput *>(context)->route_notification.store(true);
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
