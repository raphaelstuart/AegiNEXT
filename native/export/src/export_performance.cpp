#include "export_performance.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace aeginext::encode
{
ExportPerformance::ExportPerformance()
{
    const auto *value = std::getenv("AEGINEXT_EXPORT_PROFILE");
    enabled_ = value && std::strcmp(value, "1") == 0;
    if (enabled_)
    {
        start_ = std::chrono::steady_clock::now();
    }
}
void ExportPerformance::Report(uint64_t frames, uint64_t decodeNanoseconds, uint64_t downloadNanoseconds) const
{
    if (!enabled_)
    {
        return;
    }
    const auto elapsed = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start_).count();
    constexpr const char *NAMES[]{"read", "clear_overlay", "render", "upsample", "composite", "writable_frame",
        "downsample", "send_frame", "receive_packet", "mux"};
    static_assert(std::size(NAMES) == static_cast<size_t>(ExportStage::Count));
    std::fprintf(stderr, "AEGINEXT_EXPORT_PROFILE {\"frames\":%llu,\"elapsed_ms\":%.3f,\"decode_ms\":%.3f,\"download_ms\":%.3f",
        static_cast<unsigned long long>(frames), elapsed, decodeNanoseconds / 1000000.0, downloadNanoseconds / 1000000.0);
    for (size_t index = 0; index < nanoseconds_.size(); ++index)
    {
        std::fprintf(stderr, ",\"%s_ms\":%.3f", NAMES[index], nanoseconds_[index] / 1000000.0);
    }
    std::fprintf(stderr, "}\n");
}
}
