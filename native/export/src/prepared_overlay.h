#pragma once
#include "aeginext_export.h"
#include "color_pipeline.h"
#include "overlay_color_slot.h"
#include "overlay_row.h"
#include "overlay_run.h"
#include <functional>
#include <span>
#include <vector>

namespace aeginext::encode
{
class PreparedOverlay final
{
public:
    PreparedOverlay(uint32_t width, uint32_t height);
    void Accept(const an_export_overlay_info &info, std::span<const float> pixels,
        const ColorPipeline &color, double referenceWhite, const std::function<void()> &checkCancel);
    void Invalidate();
    bool IsValid() const;
    bool IsCompatible(const ColorPipeline &color, double referenceWhite) const;
    uint32_t Width() const;
    uint32_t Height() const;
    uint64_t Revision() const;
    uint64_t ActivePixels() const;
    uint64_t PreparedPixels() const;
    uint64_t ForegroundPreparations() const;
    uint64_t ScannedPixels() const;
    bool CoverageKnown() const;
    std::span<const float> Pixels() const;
    const OverlayRow &Row(size_t row) const;
    std::span<const OverlayRun> Runs(const OverlayRow &row) const;
    const PreparedForeground &Foreground(uint32_t color) const;
    size_t StorageBytes() const;

private:
    void Build(std::span<const float> pixels, const ColorPipeline &color, double referenceWhite,
        const std::function<void()> &checkCancel);
    size_t FindColor(const std::array<float, 4> &pixel, const ColorPipeline &color, double referenceWhite);

    uint32_t width_;
    uint32_t height_;
    uint64_t revision_ = 0;
    uint64_t activePixels_ = 0;
    uint64_t preparedPixels_ = 0;
    uint64_t foregroundPreparations_ = 0;
    uint64_t scannedPixels_ = 0;
    uint32_t lastState_ = 0;
    bool coverageKnown_ = false;
    bool valid_ = false;
    const ColorPipeline *color_ = nullptr;
    double referenceWhite_ = 0;
    std::span<const float> pixels_;
    std::vector<OverlayRow> rows_;
    std::vector<OverlayRun> runs_;
    std::vector<OverlayColorSlot> colors_;
};
}
