#include "hardware_decode_policy.h"
#include "media_core.h"
#include <array>
#include <cstring>
#include <iostream>
extern "C"
{
#include <libavutil/pixdesc.h>
}
using namespace aeginext::media;
namespace
{
void Require(bool value, const char *message)
{ if (!value) { throw std::runtime_error(message); } }
void HardwareDecoderUsesAnActualConfiguration()
{
#ifdef __APPLE__
    for (const auto id : {AV_CODEC_ID_H264, AV_CODEC_ID_HEVC, AV_CODEC_ID_PRORES, AV_CODEC_ID_AV1, AV_CODEC_ID_VP9})
    {
        const auto *decoder = FindHardwareDecoder(id, AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AV_PIX_FMT_VIDEOTOOLBOX);
        Require(decoder != nullptr && av_codec_is_decoder(decoder) && decoder->id == id,
            "Pinned SDK hardware decoder configuration was not selected.");
        if (id == AV_CODEC_ID_AV1) { Require(std::strcmp(decoder->name, "av1") == 0, "AV1 hardware selected the software-only dav1d decoder."); }
    }
    Require(FindHardwareDecoder(AV_CODEC_ID_VP8, AV_HWDEVICE_TYPE_VIDEOTOOLBOX, AV_PIX_FMT_VIDEOTOOLBOX) == nullptr,
        "Software-only decoder claimed VideoToolbox support.");
#else
    Require(FindHardwareDecoder(AV_CODEC_ID_H264, AV_HWDEVICE_TYPE_D3D11VA, AV_PIX_FMT_D3D11) != nullptr,
        "Pinned SDK lacks a selectable H.264 D3D11 configuration.");
    Require(FindHardwareDecoder(AV_CODEC_ID_PRORES, AV_HWDEVICE_TYPE_D3D11VA, AV_PIX_FMT_D3D11) == nullptr,
        "ProRes unexpectedly selected D3D11 decoding.");
    Require(FindHardwareDecoder(AV_CODEC_ID_PRORES, AV_HWDEVICE_TYPE_VULKAN, AV_PIX_FMT_VULKAN) != nullptr,
        "Pinned SDK lacks a selectable ProRes Vulkan configuration.");
    Require(FindHardwareDecoder(AV_CODEC_ID_HEVC, AV_HWDEVICE_TYPE_VULKAN, AV_PIX_FMT_VULKAN) != nullptr,
        "Pinned SDK lacks a selectable HEVC Vulkan configuration.");
#endif
    Require(FindHardwareDecoder(AV_CODEC_ID_NONE, AV_HWDEVICE_TYPE_NONE, AV_PIX_FMT_NONE) == nullptr,
        "Missing codec was accepted.");
}
void ReadbackPreservesTheSourceSamplingAndPrecision()
{
    for (const auto pair : {std::pair{AV_PIX_FMT_YUV420P, AV_PIX_FMT_NV12},
        std::pair{AV_PIX_FMT_YUV420P10, AV_PIX_FMT_P010}, std::pair{AV_PIX_FMT_YUV422P10, AV_PIX_FMT_P210},
        std::pair{AV_PIX_FMT_YUV444P10, AV_PIX_FMT_P410}, std::pair{AV_PIX_FMT_YUV444P12, AV_PIX_FMT_P416},
        std::pair{AV_PIX_FMT_YUV422P12, AV_PIX_FMT_P216}, std::pair{AV_PIX_FMT_YUVA444P12, AV_PIX_FMT_AYUV64},
        std::pair{AV_PIX_FMT_YUV444P12, AV_PIX_FMT_AYUV64}, std::pair{AV_PIX_FMT_RGB24, AV_PIX_FMT_BGRA}})
    {
        Require(HardwareReadbackFailure(pair.first, pair.second) == nullptr, "Faithful hardware storage was rejected.");
    }
}
void ReadbackRejectsSamplingAndPrecisionLoss()
{
    for (const auto pair : {std::pair{AV_PIX_FMT_YUV444P10, AV_PIX_FMT_P010},
        std::pair{AV_PIX_FMT_YUV422P10, AV_PIX_FMT_P010}, std::pair{AV_PIX_FMT_YUV444P12, AV_PIX_FMT_P410},
        std::pair{AV_PIX_FMT_YUVA444P12, AV_PIX_FMT_P416}, std::pair{AV_PIX_FMT_YUVA444P12, AV_PIX_FMT_AYUV},
        std::pair{AV_PIX_FMT_RGB24, AV_PIX_FMT_NV12}, std::pair{AV_PIX_FMT_YUV420P, AV_PIX_FMT_BGRA},
        std::pair{AV_PIX_FMT_GBRPF32, AV_PIX_FMT_GBRP16}, std::pair{AV_PIX_FMT_NONE, AV_PIX_FMT_NV12},
        std::pair{AV_PIX_FMT_YUV420P, AV_PIX_FMT_VIDEOTOOLBOX}})
    {
        Require(HardwareReadbackFailure(pair.first, pair.second) != nullptr, "Hardware changed source pixel semantics without rejection.");
    }
}
void ExtraAlphaIsAcceptedOnlyWhenEntirelyOpaque()
{
    FramePointer frame(av_frame_alloc());
    if (!frame) { throw std::bad_alloc(); }
    frame->format = AV_PIX_FMT_AYUV64; frame->width = 513; frame->height = 2;
    Require(av_frame_get_buffer(frame.get(), 32) == 0, "Cannot allocate alpha fixture.");
    const auto *descriptor = av_pix_fmt_desc_get(AV_PIX_FMT_AYUV64);
    std::array<uint16_t, 513> alpha;
    alpha.fill(UINT16_MAX);
    for (int row = 0; row < frame->height; ++row)
    { av_write_image_line2(alpha.data(), frame->data, frame->linesize, descriptor, 0, row, 3, frame->width, sizeof(uint16_t)); }
    Require(HardwareReadbackAlphaIsOpaque(frame.get()), "Fully opaque hardware padding alpha was rejected.");
    alpha.back() = UINT16_MAX - 1;
    std::memset(frame->data[0] + frame->linesize[0], 0, static_cast<size_t>(frame->linesize[0]));
    av_write_image_line2(alpha.data(), frame->data, frame->linesize, descriptor, 0, 1, 3, frame->width, sizeof(uint16_t));
    Require(!HardwareReadbackAlphaIsOpaque(frame.get()), "A nonopaque sample outside the first chunk was ignored.");
    Require(!HardwareReadbackAlphaIsOpaque(nullptr), "Missing alpha frame was accepted.");
}
void SetupFailuresNeverAbsorbSourceErrors()
{
    for (const auto result : {AVERROR(ENOSYS), AVERROR(ENODEV), AVERROR(ENOTSUP)})
    { Require(IsHardwareSetupFailure(result), "Unavailable hardware setup was not classified."); }
    for (const auto result : {AVERROR_INVALIDDATA, AVERROR(EINVAL), AVERROR(ENOMEM), AVERROR(EIO), AVERROR_EXIT, 0})
    { Require(!IsHardwareSetupFailure(result), "Corruption, cancellation, or memory failure was reclassified as unavailable hardware."); }
}
}
int main()
{
    int failures = 0;
    for (const auto test : {HardwareDecoderUsesAnActualConfiguration, ReadbackPreservesTheSourceSamplingAndPrecision,
        ReadbackRejectsSamplingAndPrecisionLoss, ExtraAlphaIsAcceptedOnlyWhenEntirelyOpaque, SetupFailuresNeverAbsorbSourceErrors})
    {
        try { test(); }
        catch (const std::exception &error) { ++failures; std::cerr << error.what() << '\n'; }
    }
    if (failures) { return 1; }
    std::cout << "Hardware decoder selection, faithful readback, opaque padding, and failure classification passed.\n";
    return 0;
}
