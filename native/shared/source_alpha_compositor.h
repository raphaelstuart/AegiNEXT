#pragma once
#include "media_core.h"
#include <functional>
#include <vector>

struct SwsContext;

namespace aeginext::media
{
class SourceAlphaCompositor final
{
public:
    explicit SourceAlphaCompositor(int threads = 1);
    ~SourceAlphaCompositor();
    SourceAlphaCompositor(const SourceAlphaCompositor &) = delete;
    SourceAlphaCompositor &operator=(const SourceAlphaCompositor &) = delete;
    const AVFrame *Composite(const AVFrame *source, const std::function<void()> &checkCancel = {});
    static AVPixelFormat OutputFormat(const AVFrame *source);

private:
    int threads_;
    SwsContext *normalize_ = nullptr;
    AVPixelFormat sourceFormat_ = AV_PIX_FMT_NONE;
    int width_ = 0;
    int height_ = 0;
    AVColorRange range_ = AVCOL_RANGE_UNSPECIFIED;
    AVChromaLocation chroma_ = AVCHROMA_LOC_UNSPECIFIED;
    FramePointer pixels_;
    FramePointer output_;
    std::vector<uint32_t> alpha_;
};
}
