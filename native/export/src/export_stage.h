#pragma once
#include <cstddef>

namespace aeginext::encode
{
enum class ExportStage : size_t
{
    Decode,
    PrefetchWait,
    ClearOverlay,
    Render,
    OverlayCoverage,
    Upsample,
    Composite,
    WritableFrame,
    Downsample,
    SendFrame,
    ReceivePacket,
    Mux,
    Count
};
}
