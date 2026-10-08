#pragma once
#include <cstdint>
extern "C"
{
#include <libavutil/avutil.h>
}
namespace aeginext::media
{
enum DisplayTimingEvidence : uint32_t
{
    DISPLAY_ORIGINAL_PTS = 1,
    DISPLAY_BEST_EFFORT = 2,
    DISPLAY_PREVIOUS_DURATION = 4,
    DISPLAY_DECLARED_FRAME_RATE = 8,
    DISPLAY_STREAM_START = 16
};
struct DisplayTiming
{
    int64_t value = AV_NOPTS_VALUE;
    AVRational timeBase{};
    uint32_t evidence = 0;
};
}
