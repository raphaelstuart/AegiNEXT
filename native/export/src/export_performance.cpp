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
    constexpr const char *NAMES[]{"read", "prefetch_wait", "clear_overlay", "render", "overlay_coverage", "upsample", "composite", "writable_frame",
        "downsample", "send_frame", "receive_packet", "mux"};
    static_assert(std::size(NAMES) == static_cast<size_t>(ExportStage::Count));
    std::fprintf(stderr, "AEGINEXT_EXPORT_PROFILE {\"frames\":%llu,\"elapsed_ms\":%.3f,\"decode_ms\":%.3f,\"download_ms\":%.3f",
        static_cast<unsigned long long>(frames), elapsed, decodeNanoseconds / 1000000.0, downloadNanoseconds / 1000000.0);
    for (size_t index = 0; index < nanoseconds_.size(); ++index)
    {
        std::fprintf(stderr, ",\"%s_ms\":%.3f", NAMES[index], nanoseconds_[index] / 1000000.0);
    }
    std::fprintf(stderr, ",\"overlay_updated\":%llu,\"overlay_unchanged\":%llu,\"overlay_empty\":%llu,"
        "\"coverage_pixels\":%llu,\"active_pixels\":%llu,\"prepared_pixels\":%llu,\"foreground_preparations\":%llu,"
        "\"coverage_skipped_updates\":%llu,\"coverage_deferred_builds\":%llu,\"active_pixels_unknown_frames\":%llu,"
        "\"prefetch_capacity\":%llu,\"prefetch_produced\":%llu,\"prefetch_consumed\":%llu,"
        "\"prefetch_peak_queued\":%llu,\"prefetch_peak_inflight\":%llu,\"prefetch_peak_frames\":%llu}\n",
        static_cast<unsigned long long>(overlayUpdated_), static_cast<unsigned long long>(overlayUnchanged_),
        static_cast<unsigned long long>(overlayEmpty_), static_cast<unsigned long long>(coveragePixels_),
        static_cast<unsigned long long>(activePixels_), static_cast<unsigned long long>(preparedPixels_),
        static_cast<unsigned long long>(foregroundPreparations_), static_cast<unsigned long long>(coverageSkippedUpdates_),
        static_cast<unsigned long long>(coverageDeferredBuilds_), static_cast<unsigned long long>(activePixelsUnknownFrames_),
        static_cast<unsigned long long>(prefetchEnabled_ ? prefetchStatistics_.capacity : 0),
        static_cast<unsigned long long>(prefetchStatistics_.produced), static_cast<unsigned long long>(prefetchStatistics_.consumed),
        static_cast<unsigned long long>(prefetchStatistics_.peakQueued), static_cast<unsigned long long>(prefetchStatistics_.peakInFlight),
        static_cast<unsigned long long>(prefetchStatistics_.peakPrefetched));
}
void ExportPerformance::RecordOverlay(uint32_t state, const PreparedOverlay &overlay)
{
    if (!enabled_)
    {
        return;
    }
    if (state == AN_EXPORT_OVERLAY_UPDATED)
    {
        ++overlayUpdated_;
        if (!overlay.CoverageKnown()) ++coverageSkippedUpdates_;
    }
    else if (state == AN_EXPORT_OVERLAY_UNCHANGED)
    {
        ++overlayUnchanged_;
        if (overlay.ScannedPixels()) ++coverageDeferredBuilds_;
    }
    else if (state == AN_EXPORT_OVERLAY_EMPTY)
    {
        ++overlayEmpty_;
    }
    coveragePixels_ += overlay.ScannedPixels();
    if (overlay.ScannedPixels()) foregroundPreparations_ += overlay.ForegroundPreparations();
    if (!overlay.CoverageKnown()) ++activePixelsUnknownFrames_;
    activePixels_ += overlay.ActivePixels();
    preparedPixels_ += overlay.PreparedPixels();
}
void ExportPerformance::RecordPrefetch(const DecodePrefetchStatistics &statistics)
{
    prefetchEnabled_ = true;
    prefetchStatistics_ = statistics;
}
}
