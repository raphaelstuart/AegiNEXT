#include "audio_output_timeline.h"
#include <algorithm>
#include <stdexcept>

void AudioOutputTimeline::append(int64_t device_start, int64_t media_start, int frames, int span)
{
    if (frames < 0 || span <= 0 || frames > span || media_start < 0 || count == CAPACITY)
    {
        throw std::runtime_error("Invalid or unobserved audio presentation timeline");
    }
    if (count != 0)
    {
        const auto &last = segments[(begin + count - 1) % CAPACITY];
        if (device_start < last.device_start + last.span || media_start != last.media_start + last.frames)
        {
            throw std::runtime_error("Audio presentation timeline overlaps or loses PCM");
        }
    }
    segments[(begin + count++) % CAPACITY] = {device_start, media_start, frames, span};
}

int64_t AudioOutputTimeline::read(int64_t device_frame)
{
    while (count != 0)
    {
        const auto &segment = segments[begin];
        if (device_frame < segment.device_start)
        {
            break;
        }
        const auto offset = std::clamp<int64_t>(device_frame - segment.device_start, 0, segment.frames);
        played = std::max(played, segment.media_start + offset);
        if (device_frame < segment.device_start + segment.span)
        {
            break;
        }
        begin = (begin + 1) % CAPACITY;
        --count;
    }
    return played;
}

void AudioOutputTimeline::reset()
{
    begin = count = 0;
    played = 0;
}
