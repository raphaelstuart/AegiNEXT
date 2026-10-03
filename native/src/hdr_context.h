#ifndef AEGINEXT_HDR_CONTEXT_H
#define AEGINEXT_HDR_CONTEXT_H

#include "aeginext_hdr.h"
#include "hdr_view.h"

#include <libplacebo/renderer.h>
#include <libplacebo/vulkan.h>
#include <vector>

namespace AegiNext
{
class HdrContext final
{
public:
    HdrContext() = default;
    ~HdrContext() noexcept;
    HdrContext(const HdrContext &) = delete;
    HdrContext &operator=(const HdrContext &) = delete;

    void Initialize();
    void *View() const noexcept;
    void Present(
        const uint16_t *rgba, uint64_t byteCount, uint32_t width, uint32_t height,
        uint32_t rowBytes, float sourceWhiteNits, float sourcePeakNits,
        an_hdr_status &status);
    void Verify(an_hdr_verification &result);

private:
    void UpdateDrawable(an_hdr_status &status);
    void ValidateSwapchain(const pl_swapchain_frame &frame, an_hdr_status &status);
    void UploadNormalized(const uint16_t *rgba, uint64_t byteCount,
                          uint32_t width, uint32_t height, uint32_t rowBytes,
                          float whiteNits, float peakNits);
    pl_frame SourceFrame(float peakNits) const;
    void Render(const pl_frame &source, const pl_frame &target);
    std::vector<float> RenderOffscreen(const std::vector<uint16_t> &rgba,
                                       uint32_t width, uint32_t height,
                                       float whiteNits, float peakNits, float targetPeakNits);

    ANHdrView *__strong view = nil;
    pl_log log = nullptr;
    pl_vk_inst instance = nullptr;
    VkSurfaceKHR surface = VK_NULL_HANDLE;
    PFN_vkDestroySurfaceKHR destroySurface = nullptr;
    pl_vulkan vulkan = nullptr;
    pl_swapchain swapchain = nullptr;
    pl_renderer renderer = nullptr;
    pl_tex uploadTexture = nullptr;
    pl_fmt float16Format = nullptr;
    pl_fmt float32Format = nullptr;
    std::vector<float> staging;
    uint64_t submittedFrames = 0;
};
}

#endif
