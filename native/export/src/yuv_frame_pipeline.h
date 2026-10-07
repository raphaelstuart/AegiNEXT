#pragma once
#include "color_pipeline.h"
#include "frame_row_executor.h"
#include "media_core.h"
#include <functional>
#include <span>

struct SwsContext;

namespace aeginext::encode
{
class YuvFramePipeline final
{
public:
    YuvFramePipeline(const AVFrame *source, int width, int height, AVPixelFormat outputFormat, int threads = 4);
    ~YuvFramePipeline();
    YuvFramePipeline(const YuvFramePipeline &) = delete;
    YuvFramePipeline &operator=(const YuvFramePipeline &) = delete;
    int Upsample(const AVFrame *source);
    void Composite(const AVFrame *source, std::span<const float> layer, const ColorPipeline &color,
        double referenceWhite, const std::function<void()> &checkCancel);
    int Downsample(AVFrame *output);

private:
    int width_;
    int height_;
    AVPixelFormat sourceFormat_;
    AVPixelFormat outputFormat_;
    aeginext::media::FramePointer upsampled_;
    aeginext::media::FramePointer visible_;
    SwsContext *upsample_ = nullptr;
    SwsContext *downsample_ = nullptr;
    bool ready_ = false;
    FrameRowExecutor rows_;
};
}
