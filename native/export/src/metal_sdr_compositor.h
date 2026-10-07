#pragma once

#include "metal_compositor_timing.h"
#include <functional>
#include <memory>
#include <span>
#include <string>

extern "C"
{
#include <libavutil/frame.h>
}

namespace aeginext::encode::experimental
{
struct MetalSdrCompositorState;

/// <summary>Standalone experimental float32 SDR compositor; it is not connected to the export ABI or default renderer. Calls on one instance must be serialized because its persistent buffers do not support concurrent composition.</summary>
class MetalSdrCompositor final
{
public:
    /// <summary>Reports whether an Apple-family GPU with shared CPU/GPU memory is available.</summary>
    static bool IsAvailable();
    /// <summary>Creates reusable Metal buffers and a precise-math BT709/sRGB compute pipeline.</summary>
    MetalSdrCompositor(int width, int height);
    /// <summary>Releases Metal resources after all synchronously submitted work has completed.</summary>
    ~MetalSdrCompositor();
    MetalSdrCompositor(const MetalSdrCompositor &) = delete;
    MetalSdrCompositor &operator=(const MetalSdrCompositor &) = delete;
    /// <summary>Returns the actual Metal device name for benchmark evidence.</summary>
    std::string DeviceName() const;
    /// <summary>Composites uncropped BT709 YUV444P16LE into frame memory disjoint from every source plane. Cancellation is checked before submission and after draining GPU work; failure never publishes output pixels.</summary>
    MetalCompositorTiming Composite(const AVFrame *source, std::span<const float> layer,
        double referenceWhite, AVFrame *output, const std::function<void()> &checkCancel);

private:
    std::unique_ptr<MetalSdrCompositorState> state_;
};
}
