#include "hdr_context.h"
#include "frame_conversion.h"
#include "hdr_error.h"
#include "texture_owner.h"

#import <Metal/Metal.h>
#include <MoltenVK/mvk_private_api.h>
#include <libplacebo/log.h>
#include <libplacebo/shaders/colorspace.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>

static_assert(PL_MAJOR_VER == 7 && PL_API_VER == 360);
static_assert(MVK_VERSION == MVK_MAKE_VERSION(1, 4, 2));
static_assert(VK_HEADER_VERSION_COMPLETE == VK_MAKE_API_VERSION(0, 1, 4, 357));

namespace AegiNext
{
namespace
{
template<typename Action>
void Cleanup(Action &&action) noexcept
{
    @try
    {
        try
        {
            action();
        }
        catch (...)
        {
            std::fputs("AegiNext: C++ exception while releasing an HDR resource.\n", stderr);
        }
    }
    @catch (NSException *exception)
    {
        std::fprintf(stderr, "AegiNext: HDR cleanup exception: %s\n", exception.reason.UTF8String);
    }
}

pl_color_space LinearSpace(pl_color_primaries primaries, float peakNits)
{
    pl_color_space color{};
    color.primaries = primaries;
    color.transfer = PL_COLOR_TRC_LINEAR;
    color.hdr.prim = *pl_raw_primaries_get(primaries);
    color.hdr.min_luma = 1e-6f;
    color.hdr.max_luma = peakNits;
    return color;
}

pl_frame TextureFrame(pl_tex texture, pl_color_primaries primaries, float peakNits)
{
    pl_frame frame{};
    frame.num_planes = 1;
    frame.planes[0].texture = texture;
    frame.planes[0].components = 4;
    for (auto channel = 0; channel < 4; ++channel)
    {
        frame.planes[0].component_mapping[channel] = channel;
    }

    frame.repr.sys = PL_COLOR_SYSTEM_RGB;
    frame.repr.levels = PL_COLOR_LEVELS_FULL;
    frame.repr.alpha = PL_ALPHA_PREMULTIPLIED;
    frame.color = LinearSpace(primaries, peakNits);
    return frame;
}

void Upload(pl_gpu gpu, pl_tex texture, void *pixels)
{
    pl_tex_transfer_params transfer{};
    transfer.tex = texture;
    transfer.ptr = pixels;
    transfer.no_import = true;
    if (!pl_tex_upload(gpu, &transfer))
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "GPU texture upload failed.");
    }
}

void Download(pl_gpu gpu, pl_tex texture, void *pixels)
{
    pl_tex_transfer_params transfer{};
    transfer.tex = texture;
    transfer.ptr = pixels;
    transfer.no_import = true;
    if (!pl_tex_download(gpu, &transfer))
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "GPU texture readback failed.");
    }
}

std::vector<uint16_t> SolidHalf(uint16_t red, uint16_t green, uint16_t blue)
{
    std::vector<uint16_t> pixels(3 * 2 * 4);
    for (size_t index = 0; index < pixels.size(); index += 4)
    {
        pixels[index] = red;
        pixels[index + 1] = green;
        pixels[index + 2] = blue;
        pixels[index + 3] = 0x3c00;
    }

    return pixels;
}
}

HdrContext::~HdrContext() noexcept
{
    if (vulkan)
    {
        Cleanup([&] { pl_gpu_finish(vulkan->gpu); });
        Cleanup([&] { pl_tex_destroy(vulkan->gpu, &uploadTexture); });
    }

    Cleanup([&] { pl_renderer_destroy(&renderer); });
    Cleanup([&] { pl_swapchain_destroy(&swapchain); });
    Cleanup([&] { pl_vulkan_destroy(&vulkan); });
    if (surface != VK_NULL_HANDLE && destroySurface && instance)
    {
        Cleanup([&] { destroySurface(instance->instance, surface, nullptr); });
    }

    Cleanup([&] { pl_vk_inst_destroy(&instance); });
    Cleanup([&] { pl_log_destroy(&log); });
    Cleanup([&]
    {
        [view removeFromSuperview];
        view.metalLayer.delegate = nil;
        view = nil;
    });
}

void HdrContext::Initialize()
{
    const auto runtimeVersion = pl_version();
    if (pl_fix_ver() != 1 || !runtimeVersion ||
        (std::strcmp(runtimeVersion, "v7.360.1") != 0 && std::strcmp(runtimeVersion, "7.360.1") != 0))
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The runtime libplacebo version must be 7.360.1.");
    }

    view = [[ANHdrView alloc] initWithFrame:NSZeroRect];
    if (!view)
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "The native preview view could not be allocated.");
    }

    view.wantsLayer = YES;
    auto layer = view.metalLayer;
    layer.delegate = view;
    layer.opaque = YES;
    layer.pixelFormat = MTLPixelFormatRGBA16Float;
    layer.wantsExtendedDynamicRangeContent = YES;
    layer.EDRMetadata = nil;
    auto colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceExtendedLinearDisplayP3);
    if (!colorSpace)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The extended linear Display P3 color space is unavailable.");
    }

    layer.colorspace = colorSpace;
    CGColorSpaceRelease(colorSpace);

    pl_log_params logParameters{};
    logParameters.log_cb = pl_log_simple;
    logParameters.log_priv = stderr;
    logParameters.log_level = PL_LOG_WARN;
    log = pl_log_create(PL_API_VER, &logParameters);

    const char *extensions[] =
    {
        VK_KHR_SURFACE_EXTENSION_NAME,
        VK_EXT_METAL_SURFACE_EXTENSION_NAME,
        VK_EXT_SWAPCHAIN_COLOR_SPACE_EXTENSION_NAME
    };
    auto instanceParameters = pl_vk_inst_default_params;
    instanceParameters.get_proc_addr = vkGetInstanceProcAddr;
    instanceParameters.extensions = extensions;
    instanceParameters.num_extensions = static_cast<int>(std::size(extensions));
    instance = pl_vk_inst_create(log, &instanceParameters);
    if (!instance)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "MoltenVK could not create a Vulkan instance with Metal surface and colorspace extensions.");
    }

    const auto createSurface = reinterpret_cast<PFN_vkCreateMetalSurfaceEXT>(
        vkGetInstanceProcAddr(instance->instance, "vkCreateMetalSurfaceEXT"));
    destroySurface = reinterpret_cast<PFN_vkDestroySurfaceKHR>(
        vkGetInstanceProcAddr(instance->instance, "vkDestroySurfaceKHR"));
    if (!createSurface || !destroySurface)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The MoltenVK Metal surface entry points are unavailable.");
    }

    VkMetalSurfaceCreateInfoEXT surfaceParameters{};
    surfaceParameters.sType = VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT;
    surfaceParameters.pLayer = layer;
    if (createSurface(instance->instance, &surfaceParameters, nullptr, &surface) != VK_SUCCESS)
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "MoltenVK could not create the embedded Metal surface.");
    }

    auto deviceParameters = pl_vulkan_default_params;
    deviceParameters.instance = instance->instance;
    deviceParameters.get_proc_addr = vkGetInstanceProcAddr;
    deviceParameters.surface = surface;
    vulkan = pl_vulkan_create(log, &deviceParameters);
    if (!vulkan)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "No usable MoltenVK device can present to the embedded surface.");
    }

    const auto getProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(
        vkGetInstanceProcAddr(instance->instance, "vkGetPhysicalDeviceProperties2"));
    if (!getProperties)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The Vulkan device-properties entry point is unavailable.");
    }

    VkPhysicalDeviceDriverProperties driver{};
    driver.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRIVER_PROPERTIES;
    VkPhysicalDeviceProperties2 properties{};
    properties.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2;
    properties.pNext = &driver;
    getProperties(vulkan->phys_device, &properties);
    if (driver.driverID != VK_DRIVER_ID_MOLTENVK || properties.properties.driverVersion != MVK_VERSION)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The active Vulkan driver must be MoltenVK 1.4.2.");
    }

    std::fprintf(stderr, "AegiNext HDR: runtime libplacebo %s; verified MoltenVK %s; Vulkan headers 1.4.357; GPU %s.\n",
                 runtimeVersion, MVK_VERSION_STRING, properties.properties.deviceName);

    const auto float16Caps = static_cast<pl_fmt_caps>(PL_FMT_CAP_SAMPLEABLE |
        PL_FMT_CAP_RENDERABLE | PL_FMT_CAP_BLITTABLE | PL_FMT_CAP_HOST_READABLE);
    float16Format = pl_find_fmt(vulkan->gpu, PL_FMT_FLOAT, 4, 16, 16, float16Caps);
    float32Format = pl_find_fmt(vulkan->gpu, PL_FMT_FLOAT, 4, 32, 32, PL_FMT_CAP_SAMPLEABLE);
    if (!float16Format || !float32Format)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "RGBA F16 render/readback or RGBA F32 upload is unsupported by this GPU.");
    }

    renderer = pl_renderer_create(log, vulkan->gpu);
    if (!renderer)
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "libplacebo could not create its renderer.");
    }

    pl_vulkan_swapchain_params swapchainParameters{};
    swapchainParameters.surface = surface;
    swapchainParameters.present_mode = VK_PRESENT_MODE_FIFO_KHR;
    swapchainParameters.swapchain_depth = 2;
    swapchainParameters.color_bits = 16;
    swapchainParameters.alpha_bits = 16;
    swapchain = pl_vulkan_create_swapchain(vulkan, &swapchainParameters);
    if (!swapchain)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The embedded Vulkan swapchain could not be created.");
    }

    pl_color_space hint{};
    hint.primaries = PL_COLOR_PRIM_DISPLAY_P3;
    hint.transfer = PL_COLOR_TRC_LINEAR;
    pl_swapchain_colorspace_hint(swapchain, &hint);
}

void *HdrContext::View() const noexcept
{
    return (__bridge void *)view;
}

void HdrContext::UpdateDrawable(an_hdr_status &status)
{
    auto window = view.window;
    auto screen = window.screen;
    if (!window || !screen || !window.visible || window.miniaturized || view.hiddenOrHasHiddenAncestor)
    {
        throw HdrError(AN_HDR_NOT_READY, "The native preview must be attached to a visible window.");
    }

    const auto rect = [view convertRectToBacking:view.bounds];
    const auto width = std::ceil(rect.size.width);
    const auto height = std::ceil(rect.size.height);
    const auto scale = window.backingScaleFactor;
    if (width <= 0 || height <= 0)
    {
        throw HdrError(AN_HDR_NOT_READY, "The native preview does not yet have a drawable size.");
    }

    if (!std::isfinite(width) || !std::isfinite(height) ||
        width > vulkan->gpu->limits.max_tex_2d_dim || height > vulkan->gpu->limits.max_tex_2d_dim ||
        width > std::numeric_limits<int>::max() || height > std::numeric_limits<int>::max() ||
        !std::isfinite(scale) || scale <= 0)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The native drawable dimensions or backing scale exceed GPU capabilities.");
    }

    const auto headroom = static_cast<float>(screen.maximumExtendedDynamicRangeColorComponentValue);
    const auto potential = static_cast<float>(screen.maximumPotentialExtendedDynamicRangeColorComponentValue);
    if (!std::isfinite(headroom) || headroom < 1 || !std::isfinite(potential) || potential < 1 ||
        !std::isfinite(headroom * NOMINAL_DISPLAY_WHITE))
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The screen did not report a valid relative EDR range.");
    }

    auto drawableWidth = static_cast<int>(width);
    auto drawableHeight = static_cast<int>(height);
    auto layer = view.metalLayer;
    layer.contentsScale = scale;
    layer.drawableSize = CGSizeMake(width, height);
    if (!pl_swapchain_resize(swapchain, &drawableWidth, &drawableHeight) ||
        drawableWidth <= 0 || drawableHeight <= 0)
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "The Vulkan swapchain could not resize to the native view.");
    }

    status.drawable_width = static_cast<uint32_t>(drawableWidth);
    status.drawable_height = static_cast<uint32_t>(drawableHeight);
    status.display_id = [screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue];
    status.current_headroom = headroom;
    status.potential_headroom = potential;
    status.backing_scale = static_cast<float>(scale);
    status.nominal_display_white = NOMINAL_DISPLAY_WHITE;
}

void HdrContext::ValidateSwapchain(const pl_swapchain_frame &frame, an_hdr_status &status)
{
    const auto format = frame.fbo ? frame.fbo->params.format : nullptr;
    auto valid = format && format->type == PL_FMT_FLOAT && format->num_components == 4;
    for (auto channel = 0; valid && channel < 4; ++channel)
    {
        valid = format->component_depth[channel] == 16;
    }

    auto layer = view.metalLayer;
    const auto colorName = layer.colorspace ? CGColorSpaceCopyName(layer.colorspace) : nullptr;
    const auto correctColorSpace = colorName && CFEqual(colorName, kCGColorSpaceExtendedLinearDisplayP3);
    if (colorName)
    {
        CFRelease(colorName);
    }

    if (!valid || frame.color_space.primaries != PL_COLOR_PRIM_DISPLAY_P3 ||
        frame.color_space.transfer != PL_COLOR_TRC_LINEAR ||
        layer.pixelFormat != MTLPixelFormatRGBA16Float || !correctColorSpace ||
        !layer.wantsExtendedDynamicRangeContent || layer.EDRMetadata != nil)
    {
        throw HdrError(AN_HDR_UNSUPPORTED, "The actual swapchain is not FP16 Linear Display P3 with app-managed EDR mapping.");
    }

    status.float16_verified = 1;
    status.edr_enabled = 1;
    status.drawable_width = static_cast<uint32_t>(frame.fbo->params.w);
    status.drawable_height = static_cast<uint32_t>(frame.fbo->params.h);
}

void HdrContext::UploadNormalized(const uint16_t *rgba, uint64_t byteCount,
                                  uint32_t width, uint32_t height, uint32_t rowBytes,
                                  float whiteNits, float peakNits)
{
    if (width > vulkan->gpu->limits.max_tex_2d_dim || height > vulkan->gpu->limits.max_tex_2d_dim ||
        width > static_cast<uint32_t>(std::numeric_limits<int>::max()) ||
        height > static_cast<uint32_t>(std::numeric_limits<int>::max()))
    {
        throw HdrError(AN_HDR_INVALID_ARGUMENT, "The input frame exceeds the GPU texture dimensions.");
    }

    NormalizeFrame(rgba, byteCount, width, height, rowBytes, whiteNits, peakNits, staging);
    pl_tex_params textureParameters{};
    textureParameters.w = static_cast<int>(width);
    textureParameters.h = static_cast<int>(height);
    textureParameters.format = float32Format;
    textureParameters.sampleable = true;
    textureParameters.host_writable = true;
    if (!pl_tex_recreate(vulkan->gpu, &uploadTexture, &textureParameters))
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "Could not allocate the float32 normalized upload texture.");
    }

    Upload(vulkan->gpu, uploadTexture, staging.data());
}

pl_frame HdrContext::SourceFrame(float peakNits) const
{
    return TextureFrame(uploadTexture, PL_COLOR_PRIM_BT_709, peakNits);
}

void HdrContext::Render(const pl_frame &source, const pl_frame &target)
{
    auto colorParameters = pl_color_map_default_params;
    colorParameters.metadata = PL_HDR_METADATA_HDR10;
    colorParameters.inverse_tone_mapping = false;
    colorParameters.contrast_recovery = 0;
    auto parameters = pl_render_fast_params;
    parameters.color_map_params = &colorParameters;
    parameters.peak_detect_params = nullptr;
    parameters.dither_params = nullptr;
    parameters.background = PL_CLEAR_COLOR;
    parameters.border = PL_CLEAR_COLOR;
    parameters.background_transparency = 0;
    if (!pl_render_image(renderer, &source, &target, &parameters))
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "libplacebo failed to render the linear HDR image.");
    }
}

void HdrContext::Present(
    const uint16_t *rgba, uint64_t byteCount, uint32_t width, uint32_t height,
    uint32_t rowBytes, float sourceWhiteNits, float sourcePeakNits, an_hdr_status &status)
{
    UpdateDrawable(status);
    UploadNormalized(rgba, byteCount, width, height, rowBytes, sourceWhiteNits, sourcePeakNits);
    pl_swapchain_frame frame{};
    if (!pl_swapchain_start_frame(swapchain, &frame))
    {
        throw HdrError(AN_HDR_NOT_READY, "A drawable is temporarily unavailable for the native preview.");
    }

    auto consumed = false;
    @try
    {
        try
        {
            const float black[] = {0, 0, 0, 1};
            pl_tex_clear(vulkan->gpu, frame.fbo, black);
            ValidateSwapchain(frame, status);
            auto source = SourceFrame(sourcePeakNits);
            pl_frame target{};
            pl_frame_from_swapchain(&target, &frame);
            target.color = LinearSpace(PL_COLOR_PRIM_DISPLAY_P3, NOMINAL_DISPLAY_WHITE * status.current_headroom);
            target.repr.alpha = PL_ALPHA_NONE;

            const auto sourceAspect = static_cast<float>(width) / height;
            const auto targetAspect = static_cast<float>(frame.fbo->params.w) / frame.fbo->params.h;
            if (targetAspect > sourceAspect)
            {
                const auto imageWidth = frame.fbo->params.h * sourceAspect;
                target.crop.x0 = (frame.fbo->params.w - imageWidth) * 0.5f;
                target.crop.x1 = target.crop.x0 + imageWidth;
                target.crop.y0 = 0;
                target.crop.y1 = static_cast<float>(frame.fbo->params.h);
            }
            else
            {
                const auto imageHeight = frame.fbo->params.w / sourceAspect;
                target.crop.x0 = 0;
                target.crop.x1 = static_cast<float>(frame.fbo->params.w);
                target.crop.y0 = (frame.fbo->params.h - imageHeight) * 0.5f;
                target.crop.y1 = target.crop.y0 + imageHeight;
            }

            Render(source, target);
            consumed = true;
            if (!pl_swapchain_submit_frame(swapchain))
            {
                throw HdrError(AN_HDR_NATIVE_FAILURE, "The Vulkan swapchain could not submit the rendered frame.");
            }

            pl_swapchain_swap_buffers(swapchain);
        }
        catch (...)
        {
            if (!consumed)
            {
                consumed = true;
                Cleanup([&] { pl_swapchain_submit_frame(swapchain); });
            }

            throw;
        }
    }
    @catch (NSException *exception)
    {
        if (!consumed)
        {
            Cleanup([&] { pl_swapchain_submit_frame(swapchain); });
        }

        @throw exception;
    }

    status.submitted_frames = ++submittedFrames;
    status.source_white_nits = sourceWhiteNits;
    status.source_peak_nits = sourcePeakNits;
}

std::vector<float> HdrContext::RenderOffscreen(const std::vector<uint16_t> &rgba,
                                               uint32_t width, uint32_t height,
                                               float whiteNits, float peakNits, float targetPeakNits)
{
    UploadNormalized(rgba.data(), rgba.size() * sizeof(uint16_t), width, height,
                     width * 4 * sizeof(uint16_t), whiteNits, peakNits);
    pl_tex_params targetParameters{};
    targetParameters.w = static_cast<int>(width);
    targetParameters.h = static_cast<int>(height);
    targetParameters.format = float16Format;
    targetParameters.renderable = true;
    targetParameters.blit_dst = true;
    targetParameters.host_readable = true;
    TextureOwner targetTexture(vulkan->gpu, targetParameters);
    auto source = SourceFrame(peakNits);
    auto target = TextureFrame(targetTexture.Get(), PL_COLOR_PRIM_DISPLAY_P3, targetPeakNits);
    Render(source, target);

    std::vector<uint16_t> downloaded(rgba.size());
    Download(vulkan->gpu, targetTexture.Get(), downloaded.data());
    std::vector<float> result(downloaded.size());
    std::transform(downloaded.begin(), downloaded.end(), result.begin(), HalfToFloat);
    if (std::any_of(result.begin(), result.end(), [](float value) { return !std::isfinite(value); }))
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "Offscreen GPU readback contained a non-finite component.");
    }

    return result;
}

void HdrContext::Verify(an_hdr_verification &result)
{
    std::array<uint16_t, 24> pattern
    {
        0xb400, 0x3800, 0x4000, 0x3c00,
        0x4400, 0x3c00, 0x0000, 0x3c00,
        0x0000, 0x0000, 0x0000, 0x0000,
        0x3400, 0x3800, 0xbc00, 0x3800,
        0x4200, 0x4000, 0x3c00, 0x3c00,
        0x0001, 0x8001, 0x7bff, 0x3c00
    };
    pl_tex_params uploadParameters{};
    uploadParameters.w = 3;
    uploadParameters.h = 2;
    uploadParameters.format = float16Format;
    uploadParameters.host_writable = true;
    uploadParameters.host_readable = true;
    TextureOwner roundtrip(vulkan->gpu, uploadParameters);
    Upload(vulkan->gpu, roundtrip.Get(), pattern.data());
    std::array<uint16_t, 24> downloaded{};
    Download(vulkan->gpu, roundtrip.Get(), downloaded.data());
    for (size_t index = 0; index < pattern.size(); ++index)
    {
        result.upload_max_error = std::max(result.upload_max_error,
            std::abs(HalfToFloat(pattern[index]) - HalfToFloat(downloaded[index])));
    }

    auto red = RenderOffscreen(SolidHalf(0x3c00, 0, 0), 3, 2, 203, 203, 203);
    constexpr std::array<float, 4> EXPECTED_RED{0.8225929f, 0.0331995f, 0.0170854f, 1.0f};
    for (size_t index = 0; index < red.size(); ++index)
    {
        result.primaries_max_error = std::max(result.primaries_max_error,
            std::abs(red[index] - EXPECTED_RED[index % 4]));
    }

    auto white203 = RenderOffscreen(SolidHalf(0x3c00, 0x3800, 0x3400), 3, 2, 203, 203, 812);
    auto white101 = RenderOffscreen(SolidHalf(0x4000, 0x3c00, 0x3800), 3, 2, 101.5f, 203, 812);
    for (size_t index = 0; index < white203.size(); ++index)
    {
        result.reference_white_max_error = std::max(result.reference_white_max_error,
            std::abs(white203[index] - white101[index]));
    }

    auto hdr = RenderOffscreen(SolidHalf(0x4400, 0x4400, 0x4400), 3, 2, 203, 812, 812);
    auto hdrMin = std::numeric_limits<float>::max();
    for (size_t index = 0; index < hdr.size(); ++index)
    {
        if (index % 4 != 3)
        {
            result.hdr_max_component = std::max(result.hdr_max_component, hdr[index]);
            hdrMin = std::min(hdrMin, hdr[index]);
        }
    }

    result.pipeline_ok = result.upload_max_error <= 1e-6f &&
        result.primaries_max_error <= 0.003f && result.reference_white_max_error <= 0.002f &&
        hdrMin >= 3.98f && result.hdr_max_component <= 4.02f;
    if (!result.pipeline_ok)
    {
        throw HdrError(AN_HDR_NATIVE_FAILURE, "GPU upload or offscreen linear-P3 verification exceeded its numeric tolerance.");
    }
}
}
