#pragma once

#include "frame_owner.h"

extern "C"
{
#include <libswscale/swscale.h>
}

namespace aeginext::decode
{
class PreviewConverter final
{
public:
    PreviewConverter();
    explicit PreviewConverter(int threadBudget);
    ~PreviewConverter();
    PreviewConverter(const PreviewConverter &) = delete;
    PreviewConverter &operator=(const PreviewConverter &) = delete;
    void Convert(const FrameOwner &owner, const an_preview_request &request,
        uint8_t *destination, uint64_t capacity);
    static an_preview_backend_info BackendInfo();
    int ThreadBudget() const noexcept { return cms_->threads; }

private:
    SwsContext *cms_ = nullptr;
    SwsContext *resampler_ = nullptr;
    FramePointer converted_;
    FramePointer output_;
};
}
