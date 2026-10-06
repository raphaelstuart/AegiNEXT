#include "aeginext_audio.h"
#include "audio_output_timeline.h"
#include <SDL3/SDL.h>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

static void require(bool condition, const char *message)
{
    if (!condition) { throw std::runtime_error(message); }
}
static void put(std::ofstream &file, uint32_t value, int bytes)
{
    for (int index = 0; index < bytes; ++index) { file.put(static_cast<char>((value >> (index * 8)) & 255)); }
}
int main()
{
    void *decoder = nullptr;
    void *output = nullptr;
    const auto path = std::filesystem::temp_directory_path() / "aeginext-audio-contract.wav";
    try
    {
        {
            std::ofstream file(path, std::ios::binary);
            file.write("RIFF", 4); put(file, 36 + 44100 * 2, 4); file.write("WAVEfmt ", 8);
            put(file, 16, 4); put(file, 1, 2); put(file, 1, 2); put(file, 44100, 4);
            put(file, 88200, 4); put(file, 2, 2); put(file, 16, 2); file.write("data", 4); put(file, 88200, 4);
            for (int index = 0; index < 44100; ++index)
            {
                const auto sample = static_cast<int16_t>(12000 * std::sin(index * 6.283185307179586 * 440 / 44100));
                put(file, static_cast<uint16_t>(sample), 2);
            }
        }
        char error[1024]{};
        require(an_audio_abi_version() == 2 && an_audio_clock_snapshot_size() == sizeof(an_audio_clock_snapshot), "ABI and clock layout");
        AudioOutputTimeline timeline;
        timeline.append(100, 0, 100, 200);
        require(timeline.read(99) == 0 && timeline.read(150) == 50 && timeline.read(250) == 100, "Future frames and underflow silence do not move media position");
        timeline.append(300, 100, 60, 100);
        require(timeline.read(330) == 130 && timeline.read(400) == 160, "Device position maps to real PCM after underflow");
        timeline.reset();
        require(timeline.read(10000) == 0, "Seek clears device-to-media mapping");
        require(an_audio_decoder_create(&decoder, error, sizeof(error)) == 0, error);
        require(an_audio_decoder_open(decoder, path.string().c_str(), 0, 16000, 1, error, sizeof(error)) == 0, error);
        std::vector<float> samples(4096);
        int total = 0;
        double energy = 0;
        int64_t expected = 0;
        while (true)
        {
            int frames = 0;
            int64_t start = 0;
            const auto status = an_audio_decoder_read(decoder, samples.data(), 4096, &frames, &start, error, sizeof(error));
            if (status == 1) { break; }
            require(status == 0, error);
            require(start == expected && frames > 0 && frames <= 4096, "PCM timestamp and capacity");
            expected += frames;
            total += frames;
            for (int index = 0; index < frames; ++index) { energy += samples[index] * samples[index]; }
        }
        require(total == 16000 && energy / total > 0.01, "Resampled duration and audible energy");
        require(an_audio_decoder_seek(decoder, 4000, error, sizeof(error)) == 0, error);
        int frames = 0;
        int64_t start = 0;
        require(an_audio_decoder_read(decoder, samples.data(), 4096, &frames, &start, error, sizeof(error)) == 0, error);
        require(start == 4000 && frames > 0, "Sample accurate seek trim");
        an_audio_decoder_cancel(decoder);
        require(an_audio_decoder_read(decoder, samples.data(), 4096, &frames, &start, error, sizeof(error)) == 6, "Permanent cancellation");
        an_audio_decoder_destroy(decoder); decoder = nullptr;
        std::cout << "PASS resampling, PTS, seek, cancellation\n";

        require(an_audio_output_create(&output, 48000, 2, error, sizeof(error)) == 0, error);
        std::vector<float> stereo(24000, 0.1F);
        require(an_audio_output_write(output, stereo.data(), 12000, error, sizeof(error)) == 0, error);
        require(an_audio_output_queued(output) == 12000, "Paused queue retains PCM");
        require(an_audio_output_write(output, stereo.data(), 1, error, sizeof(error)) != 0, "Output queue bounded");
        require(an_audio_output_latency(output) > 0, "Device latency estimate");
        an_audio_clock_snapshot clock{};
        clock.size = sizeof(clock);
        require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 1 && clock.backend == 3, "SDL clock is explicitly estimated");
        auto wrong_size = clock;
        wrong_size.size = 1;
        require(an_audio_output_snapshot(output, &wrong_size, error, sizeof(error)) != 0, "Clock ABI size is checked");
        require(an_audio_output_gain(output, 0.5F, error, sizeof(error)) == 0, error);
        require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
        SDL_Delay(100);
        require(an_audio_output_queued(output) < 12000, "Device consumes PCM");
        require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
        const auto paused = an_audio_output_queued(output);
        SDL_Delay(50);
        require(an_audio_output_queued(output) == paused, "Paused device freezes consumption");
        require(an_audio_output_clear(output, error, sizeof(error)) == 0 && an_audio_output_queued(output) == 0, "Seek clears device queue");
        an_audio_output_destroy(output); output = nullptr;
        if (std::getenv("AEGINEXT_RUN_SYSTEM_AUDIO_TESTS"))
        {
            require(an_audio_output_create_system(&output, 48000, 2, error, sizeof(error)) == 0, error);
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
            std::fill(stereo.begin(), stereo.end(), 0.0F);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.device_id[0] != 0, "System clock and device identity");
            require(an_audio_output_write(output, stereo.data(), 12000, error, sizeof(error)) == 0, error);
            require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
            SDL_Delay(100);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames > 0 && clock.played_frames < 12000, "System device clock advances within real PCM");
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0, error);
            const auto frozen = clock.played_frames;
            SDL_Delay(50);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.played_frames == frozen, "System pause freezes output clock");
            const auto epoch = clock.epoch;
            require(an_audio_output_clear(output, error, sizeof(error)) == 0, error);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.played_frames == 0 && clock.epoch > epoch && clock.queued_frames == 0, "System seek resets clock epoch");
            require(an_audio_output_write(output, stereo.data(), 1200, error, sizeof(error)) == 0, error);
            require(an_audio_output_pause(output, 0, error, sizeof(error)) == 0, error);
            SDL_Delay(150);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames == 1200, "Device underflow silence freezes media position");
            require(an_audio_output_write(output, stereo.data(), 4800, error, sizeof(error)) == 0, error);
            SDL_Delay(180);
            require(an_audio_output_snapshot(output, &clock, error, sizeof(error)) == 0 && clock.quality == 2 && clock.played_frames == 6000, "PCM resumes after underflow without absorbing the silent device gap");
            require(an_audio_output_pause(output, 1, error, sizeof(error)) == 0, error);
            an_audio_output_destroy(output); output = nullptr;
            std::cout << "PASS system output clock, pause, seek and device identity (silent PCM)\n";
        }
        std::filesystem::remove(path);
        std::cout << "PASS bounded SDL output, pause, clear, gain, resource release\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        an_audio_output_destroy(output);
        an_audio_decoder_destroy(decoder);
        std::filesystem::remove(path);
        std::cerr << error.what() << '\n';
        return 1;
    }
}
