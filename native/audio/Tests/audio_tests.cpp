#include "aeginext_audio.h"
#include <SDL3/SDL.h>
#include <cmath>
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
        require(an_audio_abi_version() == 1, "ABI");
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
