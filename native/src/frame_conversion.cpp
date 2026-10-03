#include "frame_conversion.h"
#include "hdr_error.h"

#include <cmath>
#include <cstddef>
#include <cstring>
#include <limits>

namespace AegiNext
{
float HalfToFloat(uint16_t bits) noexcept
{
    const auto exponent = (bits >> 10) & 0x1f;
    const auto fraction = bits & 0x3ff;
    float value;

    if (exponent == 0)
    {
        value = std::ldexp(static_cast<float>(fraction), -24);
    }
    else if (exponent == 31)
    {
        value = fraction == 0 ? std::numeric_limits<float>::infinity()
                              : std::numeric_limits<float>::quiet_NaN();
    }
    else
    {
        value = std::ldexp(static_cast<float>(1024 + fraction), exponent - 25);
    }

    return (bits & 0x8000) != 0 ? -value : value;
}

void NormalizeFrame(
    const uint16_t *rgba,
    uint64_t byteCount,
    uint32_t width,
    uint32_t height,
    uint32_t rowBytes,
    float sourceWhiteNits,
    float sourcePeakNits,
    std::vector<float> &destination)
{
    const auto packedRowBytes = static_cast<uint64_t>(width) * 4 * sizeof(uint16_t);
    if (!rgba || width == 0 || height == 0 || rowBytes < packedRowBytes ||
        rowBytes % alignof(uint16_t) != 0 ||
        reinterpret_cast<uintptr_t>(rgba) % alignof(uint16_t) != 0 ||
        !std::isfinite(sourceWhiteNits) || sourceWhiteNits <= 0 ||
        !std::isfinite(sourcePeakNits) || sourcePeakNits <= 0)
    {
        throw HdrError(AN_HDR_INVALID_ARGUMENT, "Invalid RGBA F16 dimensions, stride, alignment, or luminance range.");
    }

    const auto requiredBytes = static_cast<uint64_t>(height - 1) * rowBytes + packedRowBytes;
    const auto channelCount = static_cast<uint64_t>(width) * height * 4;
    if (requiredBytes > byteCount || requiredBytes > std::numeric_limits<size_t>::max() ||
        channelCount > destination.max_size())
    {
        throw HdrError(AN_HDR_INVALID_ARGUMENT, "The supplied buffer is too short or the frame is too large.");
    }

    destination.resize(static_cast<size_t>(channelCount));
    const auto scale = static_cast<double>(sourceWhiteNits) / NOMINAL_DISPLAY_WHITE;
    const auto sourceBytes = reinterpret_cast<const std::byte *>(rgba);

    for (uint32_t y = 0; y < height; ++y)
    {
        const auto row = reinterpret_cast<const uint16_t *>(sourceBytes + static_cast<size_t>(y) * rowBytes);
        for (uint32_t x = 0; x < width; ++x)
        {
            const auto sourceIndex = static_cast<size_t>(x) * 4;
            const auto targetIndex = (static_cast<size_t>(y) * width + x) * 4;
            const auto alpha = HalfToFloat(row[sourceIndex + 3]);
            if (!std::isfinite(alpha) || alpha < 0 || alpha > 1)
            {
                throw HdrError(AN_HDR_INVALID_ARGUMENT, "RGBA F16 alpha must be finite and between zero and one.");
            }

            for (size_t channel = 0; channel < 3; ++channel)
            {
                const auto value = HalfToFloat(row[sourceIndex + channel]);
                if (!std::isfinite(value) || (alpha == 0 && value != 0))
                {
                    throw HdrError(AN_HDR_INVALID_ARGUMENT, "RGBA F16 must contain finite, premultiplied RGB values.");
                }

                const auto normalized = static_cast<double>(value) * scale;
                const auto converted = static_cast<float>(normalized);
                if (!std::isfinite(converted) || (normalized != 0 && converted == 0))
                {
                    throw HdrError(AN_HDR_INVALID_ARGUMENT, "Reference-white conversion exceeds the float32 representation.");
                }

                if (alpha > 0 && static_cast<double>(value) * sourceWhiteNits >
                    static_cast<double>(sourcePeakNits) * alpha * 1.001)
                {
                    throw HdrError(AN_HDR_INVALID_ARGUMENT, "source_peak_nits understates the supplied frame's RGB range.");
                }

                destination[targetIndex + channel] = converted;
            }

            destination[targetIndex + 3] = alpha;
        }
    }
}
}
