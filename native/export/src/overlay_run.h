#pragma once
#include <cstdint>

namespace aeginext::encode
{
struct OverlayRun final
{
    uint32_t begin;
    uint32_t end;
    uint32_t color;
};
}
