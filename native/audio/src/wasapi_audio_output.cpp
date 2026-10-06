#include "audio_output.h"
#include "audio_output_timeline.h"
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <audioclient.h>
#include <avrt.h>
#include <ks.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <exception>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>

namespace
{
constexpr int SOURCE_RATE = 48000;
constexpr int SOURCE_CHANNELS = 2;
constexpr int CAPACITY = 12000;
constexpr uint64_t HOST_FREQUENCY = 10000000;

void check_wasapi(HRESULT status, const char *operation)
{
    if (FAILED(status))
    {
        throw std::runtime_error(std::string(operation) + " (" + std::to_string(static_cast<uint32_t>(status)) + ")");
    }
}

HANDLE create_event()
{
    const auto event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!event)
    {
        throw std::runtime_error("Create WASAPI worker event (" + std::to_string(GetLastError()) + ")");
    }
    return event;
}

int64_t source_frame(uint64_t position, uint64_t frequency)
{
    const auto seconds = position / frequency;
    const auto remainder = position % frequency;
    if (seconds > static_cast<uint64_t>(std::numeric_limits<int64_t>::max() / SOURCE_RATE)
        || remainder > std::numeric_limits<uint64_t>::max() / SOURCE_RATE)
    {
        throw std::runtime_error("WASAPI audio clock exceeds source timeline range");
    }
    const auto frame = seconds * SOURCE_RATE + remainder * SOURCE_RATE / frequency;
    if (frame > static_cast<uint64_t>(std::numeric_limits<int64_t>::max()))
    {
        throw std::runtime_error("WASAPI audio clock exceeds source timeline range");
    }
    return static_cast<int64_t>(frame);
}

class WasapiRouteSignal final
{
public:
    WasapiRouteSignal() : event(create_event()) {}
    ~WasapiRouteSignal() { CloseHandle(event); }
    const HANDLE event;
    std::atomic_bool invalidated{false};
};

class WasapiRouteNotifications final : public IMMNotificationClient
{
public:
    WasapiRouteNotifications(std::wstring id, std::shared_ptr<WasapiRouteSignal> signal)
        : device_id(std::move(id)), signal(std::move(signal)) {}

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void **result) override
    {
        if (!result) { return E_POINTER; }
        *result = nullptr;
        if (id == __uuidof(IUnknown) || id == __uuidof(IMMNotificationClient))
        {
            *result = static_cast<IMMNotificationClient *>(this);
            AddRef();
            return S_OK;
        }
        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }

    ULONG STDMETHODCALLTYPE Release() override
    {
        const auto remaining = --references;
        if (remaining == 0) { delete this; }
        return remaining;
    }

    HRESULT STDMETHODCALLTYPE OnDefaultDeviceChanged(EDataFlow flow, ERole role, LPCWSTR id) override
    {
        if (flow == eRender && role == eMultimedia && !matches(id)) { invalidate(); }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnDeviceStateChanged(LPCWSTR id, DWORD state) override
    {
        if (matches(id) && state != DEVICE_STATE_ACTIVE) { invalidate(); }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnDeviceRemoved(LPCWSTR id) override
    {
        if (matches(id)) { invalidate(); }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE OnDeviceAdded(LPCWSTR) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnPropertyValueChanged(LPCWSTR, const PROPERTYKEY) override { return S_OK; }

private:
    std::atomic<ULONG> references{1};
    const std::wstring device_id;
    const std::shared_ptr<WasapiRouteSignal> signal;

    bool matches(LPCWSTR id) const { return id && device_id == id; }

    void invalidate()
    {
        signal->invalidated.store(true);
        SetEvent(signal->event);
    }
};

class WasapiStream final
{
public:
    explicit WasapiStream(std::shared_ptr<WasapiRouteSignal> route, HANDLE event) : route(std::move(route))
    {
        try
        {
            check_wasapi(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
                __uuidof(IMMDeviceEnumerator), reinterpret_cast<void **>(&enumerator)), "Create WASAPI device enumerator");
            check_wasapi(enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &device), "Read default WASAPI output device");
            LPWSTR id = nullptr;
            check_wasapi(device->GetId(&id), "Read WASAPI endpoint ID");
            try
            {
                device_id = id;
                const auto length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, id, -1, nullptr, 0, nullptr, nullptr);
                if (length <= 0 || length > static_cast<int>(encoded_id.size()))
                {
                    throw std::runtime_error("WASAPI endpoint ID cannot fit the UTF-8 clock snapshot");
                }
                if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, id, -1, encoded_id.data(),
                    static_cast<int>(encoded_id.size()), nullptr, nullptr) != length)
                {
                    throw std::runtime_error("Encode WASAPI endpoint ID");
                }
            }
            catch (...) { CoTaskMemFree(id); throw; }
            CoTaskMemFree(id);
            notifications = new WasapiRouteNotifications(device_id, this->route);
            check_wasapi(enumerator->RegisterEndpointNotificationCallback(notifications), "Observe WASAPI output route");
            registered = true;
            check_wasapi(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr,
                reinterpret_cast<void **>(&client)), "Activate WASAPI audio client");
            WAVEFORMATEX *mix = nullptr;
            check_wasapi(client->GetMixFormat(&mix), "Read WASAPI output mix format");
            output_rate = static_cast<int>(mix->nSamplesPerSec);
            output_channels = mix->nChannels;
            CoTaskMemFree(mix);
            if (output_rate <= 0 || output_channels <= 0) { throw std::runtime_error("WASAPI output mix format is invalid"); }

            WAVEFORMATEXTENSIBLE format{};
            format.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
            format.Format.nChannels = SOURCE_CHANNELS;
            format.Format.nSamplesPerSec = SOURCE_RATE;
            format.Format.wBitsPerSample = 32;
            format.Format.nBlockAlign = static_cast<WORD>(SOURCE_CHANNELS * sizeof(float));
            format.Format.nAvgBytesPerSec = SOURCE_RATE * format.Format.nBlockAlign;
            format.Format.cbSize = static_cast<WORD>(sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX));
            format.Samples.wValidBitsPerSample = 32;
            format.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
            format.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            check_wasapi(client->Initialize(AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY | AUDCLNT_STREAMFLAGS_NOPERSIST,
                0, 0, &format.Format, nullptr), "Initialize WASAPI 48 kHz stereo float stream");
            check_wasapi(client->SetEventHandle(event), "Set WASAPI rendering event");
            check_wasapi(client->GetBufferSize(&buffer_frames), "Read WASAPI buffer size");
            if (buffer_frames == 0 || buffer_frames > CAPACITY)
            {
                throw std::runtime_error("WASAPI system buffer exceeds the bounded PCM queue");
            }
            check_wasapi(client->GetService(__uuidof(IAudioRenderClient), reinterpret_cast<void **>(&render)), "Read WASAPI render client");
            check_wasapi(client->GetService(__uuidof(IAudioClock), reinterpret_cast<void **>(&clock)), "Read WASAPI stream clock");
            check_wasapi(client->GetService(__uuidof(IAudioStreamVolume), reinterpret_cast<void **>(&volume)), "Read WASAPI stream volume");
            UINT32 volume_channels = 0;
            check_wasapi(volume->GetChannelCount(&volume_channels), "Read WASAPI stream volume channel count");
            if (volume_channels != SOURCE_CHANNELS) { throw std::runtime_error("WASAPI source stream channel count changed"); }
            const std::array<float, SOURCE_CHANNELS> levels{1.0F, 1.0F};
            check_wasapi(volume->SetAllVolumes(SOURCE_CHANNELS, levels.data()), "Initialize WASAPI stream volume");
            check_wasapi(clock->GetFrequency(&clock_frequency), "Read WASAPI stream clock frequency");
            if (clock_frequency == 0) { throw std::runtime_error("WASAPI stream clock frequency is zero"); }
            confirm_default_route();
        }
        catch (...) { release(); throw; }
    }

    ~WasapiStream() { release(); }

    IMMDeviceEnumerator *enumerator = nullptr;
    IMMDevice *device = nullptr;
    IAudioClient *client = nullptr;
    IAudioRenderClient *render = nullptr;
    IAudioClock *clock = nullptr;
    IAudioStreamVolume *volume = nullptr;
    const std::shared_ptr<WasapiRouteSignal> route;
    std::array<char, 512> encoded_id{};
    UINT32 buffer_frames = 0;
    uint64_t clock_frequency = 0;
    int output_rate = 0;
    int output_channels = 0;
    bool running = false;

private:
    std::wstring device_id;
    WasapiRouteNotifications *notifications = nullptr;
    bool registered = false;

    void confirm_default_route()
    {
        IMMDevice *current = nullptr;
        check_wasapi(enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &current), "Confirm default WASAPI output route");
        LPWSTR id = nullptr;
        const auto status = current->GetId(&id);
        current->Release();
        check_wasapi(status, "Confirm WASAPI endpoint ID");
        const auto matches = device_id == id;
        CoTaskMemFree(id);
        if (!matches) { route->invalidated.store(true); }
    }

    void release()
    {
        if (running && client) { client->Stop(); }
        if (registered && enumerator) { enumerator->UnregisterEndpointNotificationCallback(notifications); }
        if (notifications) { notifications->Release(); notifications = nullptr; }
        if (clock) { clock->Release(); clock = nullptr; }
        if (volume) { volume->Release(); volume = nullptr; }
        if (render) { render->Release(); render = nullptr; }
        if (client) { client->Release(); client = nullptr; }
        if (device) { device->Release(); device = nullptr; }
        if (enumerator) { enumerator->Release(); enumerator = nullptr; }
    }
};

enum class WasapiCommand { NONE, PAUSE, RESUME, CLEAR, GAIN, SNAPSHOT, STOP };

class WasapiAudioOutput final : public AudioOutput
{
public:
    WasapiAudioOutput() : route(std::make_shared<WasapiRouteSignal>()), command_event(create_event())
    {
        try
        {
            audio_event = create_event();
            worker = std::thread([this] { run(); });
            std::unique_lock state(gate);
            changed.wait(state, [this] { return initialized; });
            if (initialization_error)
            {
                const auto error = initialization_error;
                state.unlock();
                worker.join();
                std::rethrow_exception(error);
            }
        }
        catch (...)
        {
            if (audio_event) { CloseHandle(audio_event); }
            CloseHandle(command_event);
            throw;
        }
    }

    ~WasapiAudioOutput() override
    {
        if (worker.joinable())
        {
            try { execute(WasapiCommand::STOP); } catch (...) {}
            worker.join();
        }
        CloseHandle(audio_event);
        CloseHandle(command_event);
    }

    void write(const float *samples, int frames) override
    {
        std::scoped_lock operation(api_gate);
        std::scoped_lock state(gate);
        require_valid();
        if (!samples || frames <= 0 || frames > CAPACITY) { throw std::runtime_error("Invalid WASAPI PCM write"); }
        if (submitted - played + frames > CAPACITY) { throw std::runtime_error("WASAPI PCM queue capacity exceeded"); }
        for (int frame = 0; frame < frames; ++frame)
        {
            const auto destination = static_cast<size_t>((ring_start + ring_count + frame) % CAPACITY) * SOURCE_CHANNELS;
            pcm[destination] = samples[frame * SOURCE_CHANNELS];
            pcm[destination + 1] = samples[frame * SOURCE_CHANNELS + 1];
        }
        ring_count += frames;
        submitted += frames;
    }

    void pause(bool paused) override
    {
        std::scoped_lock operation(api_gate);
        execute(paused ? WasapiCommand::PAUSE : WasapiCommand::RESUME);
    }

    void clear() override
    {
        std::scoped_lock operation(api_gate);
        execute(WasapiCommand::CLEAR);
    }

    void gain(float value) override
    {
        std::scoped_lock operation(api_gate);
        {
            std::scoped_lock state(gate);
            require_valid();
            output_gain = value;
        }
        execute(WasapiCommand::GAIN);
    }

    an_audio_clock_snapshot snapshot() override
    {
        std::scoped_lock operation(api_gate);
        execute(WasapiCommand::SNAPSHOT);
        std::scoped_lock state(gate);
        auto result = cached;
        result.quality = route->invalidated.load() ? 0 : result.quality;
        result.played_frames = played;
        result.queued_frames = static_cast<int>(std::clamp<int64_t>(submitted - played, 0, CAPACITY));
        result.epoch = epoch;
        return result;
    }

    int latency() const override { return 0; }

private:
    const std::shared_ptr<WasapiRouteSignal> route;
    const HANDLE command_event;
    HANDLE audio_event = nullptr;
    std::thread worker;
    std::mutex api_gate;
    std::mutex gate;
    std::condition_variable changed;
    std::array<float, CAPACITY * SOURCE_CHANNELS> pcm{};
    AudioOutputTimeline timeline;
    an_audio_clock_snapshot cached{};
    WasapiCommand command = WasapiCommand::NONE;
    uint64_t requested = 0;
    uint64_t completed = 0;
    std::exception_ptr command_error;
    std::exception_ptr initialization_error;
    bool initialized = false;
    bool worker_exited = false;
    bool paused = true;
    float output_gain = 1.0F;
    int ring_start = 0;
    int ring_count = 0;
    int64_t submitted = 0;
    int64_t rendered = 0;
    int64_t device_submitted = 0;
    int64_t played = 0;
    int64_t device_played = 0;
    uint64_t epoch = 0;

    void require_valid() const
    {
        if (route->invalidated.load()) { throw std::runtime_error("WASAPI output route or timeline changed; reopen the device"); }
        if (worker_exited) { throw std::runtime_error("WASAPI output worker is unavailable"); }
    }

    void execute(WasapiCommand value)
    {
        std::unique_lock state(gate);
        if (worker_exited)
        {
            if (value == WasapiCommand::STOP || value == WasapiCommand::SNAPSHOT) { return; }
            throw std::runtime_error("WASAPI output worker is unavailable");
        }
        command = value;
        command_error = nullptr;
        const auto sequence = ++requested;
        SetEvent(command_event);
        changed.wait(state, [this, sequence] { return completed >= sequence || worker_exited; });
        if (command_error) { std::rethrow_exception(command_error); }
        if (worker_exited && completed < sequence && value != WasapiCommand::STOP && value != WasapiCommand::SNAPSHOT)
        {
            throw std::runtime_error("WASAPI output worker stopped before completing the command");
        }
    }

    bool update_clock(WasapiStream &stream)
    {
        UINT64 position = 0;
        UINT64 host = 0;
        HRESULT status = S_FALSE;
        for (int attempt = 0; attempt < 3 && status == S_FALSE; ++attempt)
        {
            status = stream.clock->GetPosition(&position, &host);
        }
        if (status != S_OK)
        {
            cached.quality = 0;
            if (FAILED(status)) { check_wasapi(status, "Read WASAPI correlated stream position"); }
            return false;
        }
        const auto frame = source_frame(position, stream.clock_frequency);
        if (frame < device_played || frame > device_submitted)
        {
            throw std::runtime_error("WASAPI device clock lost its submitted PCM presentation timeline");
        }
        device_played = frame;
        played = timeline.read(frame);
        cached.quality = 2;
        cached.host_timestamp = host;
        return true;
    }

    void pump(WasapiStream &stream)
    {
        std::scoped_lock state(gate);
        require_valid();
        update_clock(stream);
        UINT32 padding = 0;
        check_wasapi(stream.client->GetCurrentPadding(&padding), "Read WASAPI render padding");
        if (padding > stream.buffer_frames) { throw std::runtime_error("WASAPI render padding exceeds buffer capacity"); }
        const auto available = stream.buffer_frames - padding;
        if (available == 0) { return; }
        BYTE *buffer = nullptr;
        check_wasapi(stream.render->GetBuffer(available, &buffer), "Acquire WASAPI render buffer");
        const auto frames = std::min(ring_count, static_cast<int>(available));
        auto *destination = reinterpret_cast<float *>(buffer);
        std::fill_n(destination, static_cast<size_t>(available) * SOURCE_CHANNELS, 0.0F);
        for (int frame = 0; frame < frames; ++frame)
        {
            const auto source = static_cast<size_t>((ring_start + frame) % CAPACITY) * SOURCE_CHANNELS;
            destination[frame * SOURCE_CHANNELS] = pcm[source];
            destination[frame * SOURCE_CHANNELS + 1] = pcm[source + 1];
        }
        check_wasapi(stream.render->ReleaseBuffer(available, frames == 0 ? AUDCLNT_BUFFERFLAGS_SILENT : 0), "Submit WASAPI render buffer");
        timeline.append(device_submitted, rendered, frames, static_cast<int>(available));
        device_submitted += available;
        rendered += frames;
        ring_start = (ring_start + frames) % CAPACITY;
        ring_count -= frames;
    }

    bool process_command(WasapiStream &stream)
    {
        WasapiCommand value;
        uint64_t sequence;
        {
            std::scoped_lock state(gate);
            value = std::exchange(command, WasapiCommand::NONE);
            sequence = requested;
        }
        if (value == WasapiCommand::NONE) { return true; }
        std::exception_ptr error;
        auto rejected_clear = false;
        try
        {
            if (value == WasapiCommand::STOP)
            {
                if (stream.running) { stream.client->Stop(); stream.running = false; }
            }
            else if (value == WasapiCommand::SNAPSHOT)
            {
                std::scoped_lock state(gate);
                if (!route->invalidated.load()) { update_clock(stream); }
            }
            else if (value == WasapiCommand::PAUSE)
            {
                if (stream.running)
                {
                    check_wasapi(stream.client->Stop(), "Pause WASAPI output");
                    stream.running = false;
                }
                std::scoped_lock state(gate);
                paused = true;
                if (!route->invalidated.load()) { update_clock(stream); }
            }
            else if (value == WasapiCommand::RESUME)
            {
                {
                    std::scoped_lock state(gate);
                    require_valid();
                }
                if (!stream.running)
                {
                    pump(stream);
                    check_wasapi(stream.client->Start(), "Start WASAPI output");
                    stream.running = true;
                    std::scoped_lock state(gate);
                    paused = false;
                }
            }
            else if (value == WasapiCommand::CLEAR)
            {
                std::scoped_lock state(gate);
                require_valid();
                if (!paused || stream.running)
                {
                    rejected_clear = true;
                    throw std::runtime_error("WASAPI Clear requires paused output");
                }
                check_wasapi(stream.client->Reset(), "Reset WASAPI output and stream clock");
                timeline.reset();
                ring_start = ring_count = 0;
                submitted = rendered = played = device_submitted = device_played = 0;
                ++epoch;
                update_clock(stream);
            }
            else if (value == WasapiCommand::GAIN)
            {
                std::scoped_lock state(gate);
                require_valid();
                const std::array<float, SOURCE_CHANNELS> levels{output_gain, output_gain};
                check_wasapi(stream.volume->SetAllVolumes(SOURCE_CHANNELS, levels.data()), "Set WASAPI output gain");
            }
        }
        catch (...)
        {
            error = std::current_exception();
            if (!rejected_clear) { invalidate(stream); }
            if (value == WasapiCommand::SNAPSHOT) { error = nullptr; }
        }
        {
            std::scoped_lock state(gate);
            command_error = error;
            completed = sequence;
            changed.notify_all();
        }
        return value != WasapiCommand::STOP;
    }

    void invalidate(WasapiStream &stream)
    {
        route->invalidated.store(true);
        if (stream.running) { stream.client->Stop(); stream.running = false; }
        std::scoped_lock state(gate);
        paused = true;
        cached.quality = 0;
    }

    void run() noexcept
    {
        const auto apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        DWORD task_index = 0;
        const auto task = SUCCEEDED(apartment) ? AvSetMmThreadCharacteristicsW(L"Audio", &task_index) : nullptr;
        try
        {
            check_wasapi(apartment, "Initialize WASAPI worker COM apartment");
            WasapiStream stream(route, audio_event);
            {
                std::scoped_lock state(gate);
                cached.size = static_cast<uint32_t>(sizeof(cached));
                cached.host_frequency = HOST_FREQUENCY;
                cached.sample_rate = stream.output_rate;
                cached.channels = stream.output_channels;
                cached.backend = 2;
                std::memcpy(cached.device_id, stream.encoded_id.data(), stream.encoded_id.size());
                update_clock(stream);
                initialized = true;
                changed.notify_all();
            }
            const std::array<HANDLE, 3> events{command_event, route->event, audio_event};
            auto running = true;
            while (running)
            {
                const auto signaled = MsgWaitForMultipleObjectsEx(static_cast<DWORD>(events.size()), events.data(), INFINITE,
                    QS_ALLINPUT, MWMO_INPUTAVAILABLE);
                if (signaled == WAIT_OBJECT_0)
                {
                    running = process_command(stream);
                }
                else if (signaled == WAIT_OBJECT_0 + 1)
                {
                    invalidate(stream);
                }
                else if (signaled == WAIT_OBJECT_0 + 2)
                {
                    if (stream.running && !route->invalidated.load())
                    {
                        try { pump(stream); } catch (...) { invalidate(stream); }
                    }
                }
                else if (signaled == WAIT_OBJECT_0 + events.size())
                {
                    MSG message{};
                    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
                    {
                        TranslateMessage(&message);
                        DispatchMessageW(&message);
                    }
                }
                else
                {
                    throw std::runtime_error("Wait for WASAPI worker event failed (" + std::to_string(GetLastError()) + ")");
                }
                if (running && route->invalidated.load())
                {
                    invalidate(stream);
                }
                else if (running && stream.running && WaitForSingleObject(audio_event, 0) == WAIT_OBJECT_0)
                {
                    try { pump(stream); } catch (...) { invalidate(stream); }
                }
            }
        }
        catch (...)
        {
            std::scoped_lock state(gate);
            route->invalidated.store(true);
            cached.quality = 0;
            if (!initialized) { initialization_error = std::current_exception(); }
            initialized = true;
        }
        if (task) { AvRevertMmThreadCharacteristics(task); }
        if (SUCCEEDED(apartment)) { CoUninitialize(); }
        std::scoped_lock state(gate);
        worker_exited = true;
        changed.notify_all();
    }
};
}

std::unique_ptr<AudioOutput> create_system_audio_output()
{
    return std::make_unique<WasapiAudioOutput>();
}
