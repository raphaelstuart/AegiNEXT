#pragma once
#include "prepared_foreground.h"
#include <array>
#include <cstdint>

namespace aeginext::encode
{
struct OverlayColorSlot final
{
    std::array<uint32_t, 4> key{};
    PreparedForeground foreground{};
    bool occupied = false;
};
}
