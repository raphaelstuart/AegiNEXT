#pragma once
#include <cstddef>
#include <cstdint>

namespace aeginext::encode
{
struct OverlayRow final
{
    uint32_t begin = 0;
    uint32_t end = 0;
    size_t firstRun = 0;
    size_t runCount = 0;
};
}
