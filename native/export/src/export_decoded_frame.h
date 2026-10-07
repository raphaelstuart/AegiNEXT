#pragma once
#include "media_core.h"

namespace aeginext::encode
{
struct ExportDecodedFrame final
{
    aeginext::media::FramePointer frame;
    AVRational timeBase{0, 1};
    uint32_t inferredFields = 0;
};
}
