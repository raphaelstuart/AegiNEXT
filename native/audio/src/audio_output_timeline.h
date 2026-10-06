#pragma once
#include <array>
#include <cstdint>

struct AudioOutputSegment
{
    int64_t device_start = 0;
    int64_t media_start = 0;
    int frames = 0;
    int span = 0;
};

class AudioOutputTimeline
{
public:
    void append(int64_t device_start, int64_t media_start, int frames, int span);
    int64_t read(int64_t device_frame);
    void reset();
private:
    static constexpr size_t CAPACITY = 256;
    std::array<AudioOutputSegment, CAPACITY> segments{};
    size_t begin = 0;
    size_t count = 0;
    int64_t played = 0;
};
