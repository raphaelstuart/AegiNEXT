#pragma once

#include "color_pipeline.h"
#include <span>

extern "C"
{
#include <libavutil/frame.h>
#include <libswscale/swscale.h>
}

namespace aeginext::encode::tests
{
/// <summary>Frozen pre-optimization scaler, cropped copy and scalar composition used only as a regression oracle.</summary>
class LegacyYuvFramePipeline final
{
public:
    /// <summary>Creates the original single-threaded full-canvas YUV444 intermediate surfaces.</summary>
    LegacyYuvFramePipeline(const AVFrame *source, int width, int height, AVPixelFormat outputFormat);
    /// <summary>Releases the oracle's private frames and scaler contexts.</summary>
    ~LegacyYuvFramePipeline();
    LegacyYuvFramePipeline(const LegacyYuvFramePipeline &) = delete;
    LegacyYuvFramePipeline &operator=(const LegacyYuvFramePipeline &) = delete;
    /// <summary>Runs the original conversion into the caller-owned encoder input frame.</summary>
    int Convert(const AVFrame *source, std::span<const float> layer, const ColorPipeline &color,
        double referenceWhite, AVFrame *output);

private:
    void Release() noexcept;
    int width_;
    int height_;
    AVFrame *upsampled_ = nullptr;
    AVFrame *composed_ = nullptr;
    SwsContext *upsample_ = nullptr;
    SwsContext *downsample_ = nullptr;
};
}
