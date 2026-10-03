#ifndef AEGINEXT_FRAME_CONVERSION_H
#define AEGINEXT_FRAME_CONVERSION_H

#include <cstdint>
#include <vector>

namespace AegiNext
{
inline constexpr float NOMINAL_DISPLAY_WHITE = 203.0f;

float HalfToFloat(uint16_t bits) noexcept;

void NormalizeFrame(
    const uint16_t *rgba,
    uint64_t byteCount,
    uint32_t width,
    uint32_t height,
    uint32_t rowBytes,
    float sourceWhiteNits,
    float sourcePeakNits,
    std::vector<float> &destination);
}

#endif
