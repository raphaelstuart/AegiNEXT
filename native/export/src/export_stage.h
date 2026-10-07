#pragma once
#include <cstddef>

namespace aeginext::encode
{
enum class ExportStage : size_t
{
    Decode,
    ClearOverlay,
    Render,
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
