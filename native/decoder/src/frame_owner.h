#pragma once

#include "decode_support.h"
#include <array>

extern "C"
{
#include <libavutil/pixdesc.h>
}

namespace aeginext::decode
{
class FrameOwner final
{
public:
    FrameOwner(FramePointer frame, AVRational streamTimeBase);
    const an_frame_info &Info() const noexcept { return info_; }
    const an_frame_hdr_info &Hdr() const noexcept { return hdr_; }
    const AVFrame *NativeFrame() const noexcept { return frame_.get(); }
    an_frame_plane_info Plane(uint32_t index) const;
    const char *SideDataName(uint32_t index) const;
    void CopyPlane(uint32_t index, uint8_t *destination, uint64_t capacity) const;

private:
    void ReadPlanes(const AVPixFmtDescriptor *descriptor);
    void ReadMetadata();
    void ValidatePlane(uint32_t index) const;
    FramePointer frame_;
    an_frame_info info_{};
    an_frame_hdr_info hdr_{};
    std::array<an_frame_plane_info, 4> planes_{};
};
}
