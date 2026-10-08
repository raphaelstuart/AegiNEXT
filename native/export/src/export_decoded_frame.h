#pragma once
#include "media_core.h"
#include "display_timing.h"

namespace aeginext::encode
{
struct ExportDecodedFrame final
{
    aeginext::media::FramePointer frame;
    AVRational timeBase{0, 1};
    uint32_t inferredFields = 0;
    aeginext::media::DisplayTiming displayTiming;
};
}
