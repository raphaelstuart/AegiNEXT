#include "hardware_decode_policy.h"
#include <algorithm>
#include <array>
extern "C"
{
#include <libavutil/pixdesc.h>
}
namespace aeginext::media
{
namespace
{
bool HasConfiguration(const AVCodec *decoder, AVHWDeviceType device, AVPixelFormat format) noexcept
{
    if (!decoder) { return false; }
    for (int index = 0; const auto *config = avcodec_get_hw_config(decoder, index); ++index)
    {
        if (config->device_type == device && config->pix_fmt == format &&
            (config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX)) { return true; }
    }
    return false;
}
bool IntegerColor(const AVPixFmtDescriptor *descriptor) noexcept
{
    constexpr auto rejected = AV_PIX_FMT_FLAG_HWACCEL | AV_PIX_FMT_FLAG_FLOAT | AV_PIX_FMT_FLAG_PAL |
        AV_PIX_FMT_FLAG_BAYER | AV_PIX_FMT_FLAG_BITSTREAM | AV_PIX_FMT_FLAG_XYZ;
    if (!descriptor || (descriptor->flags & rejected) || (descriptor->nb_components != 3 && descriptor->nb_components != 4))
    { return false; }
    for (int index = 0; index < descriptor->nb_components; ++index)
    {
        if (descriptor->comp[index].depth < 8 || descriptor->comp[index].depth > 16) { return false; }
    }
    return true;
}
}
const AVCodec *FindHardwareDecoder(AVCodecID codec, AVHWDeviceType device, AVPixelFormat format) noexcept
{
    const auto *preferred = avcodec_find_decoder(codec);
    if (HasConfiguration(preferred, device, format)) { return preferred; }
    void *iterator = nullptr;
    while (const auto *candidate = av_codec_iterate(&iterator))
    {
        if (av_codec_is_decoder(candidate) && candidate->id == codec && HasConfiguration(candidate, device, format))
        { return candidate; }
    }
    return nullptr;
}
const char *HardwareReadbackFailure(AVPixelFormat source, AVPixelFormat output) noexcept
{
    const auto *sourceDescriptor = av_pix_fmt_desc_get(source);
    const auto *outputDescriptor = av_pix_fmt_desc_get(output);
    if (!IntegerColor(sourceDescriptor) || !IntegerColor(outputDescriptor))
    { return "Hardware readback requires known integer RGB or YUV source and output formats."; }
    if ((sourceDescriptor->flags & AV_PIX_FMT_FLAG_RGB) != (outputDescriptor->flags & AV_PIX_FMT_FLAG_RGB))
    { return "Hardware readback changed the source RGB/YUV color domain."; }
    if (sourceDescriptor->log2_chroma_w != outputDescriptor->log2_chroma_w ||
        sourceDescriptor->log2_chroma_h != outputDescriptor->log2_chroma_h)
    { return "Hardware readback changed the source chroma sampling."; }
    if ((sourceDescriptor->flags & AV_PIX_FMT_FLAG_ALPHA) && !(outputDescriptor->flags & AV_PIX_FMT_FLAG_ALPHA))
    { return "Hardware readback discarded the source alpha channel."; }
    for (int index = 0; index < sourceDescriptor->nb_components; ++index)
    {
        if (index >= outputDescriptor->nb_components || outputDescriptor->comp[index].depth < sourceDescriptor->comp[index].depth)
        { return "Hardware readback reduced the source component precision."; }
    }
    return nullptr;
}
bool HardwareReadbackAlphaIsOpaque(const AVFrame *frame) noexcept
{
    if (!frame || frame->width <= 0 || frame->height <= 0) { return false; }
    const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(frame->format));
    if (!IntegerColor(descriptor) || !(descriptor->flags & AV_PIX_FMT_FLAG_ALPHA) || descriptor->nb_components != 4)
    { return false; }
    const auto &component = descriptor->comp[3];
    if (!frame->data[component.plane] || !frame->linesize[component.plane]) { return false; }
    const auto maximum = (uint32_t{1} << component.depth) - 1;
    const uint8_t *data[]{frame->data[0], frame->data[1], frame->data[2], frame->data[3]};
    std::array<uint16_t, 256> samples;
    for (int row = 0; row < frame->height; ++row)
    {
        for (int column = 0; column < frame->width; column += static_cast<int>(samples.size()))
        {
            const auto count = std::min(frame->width - column, static_cast<int>(samples.size()));
            av_read_image_line2(samples.data(), data, frame->linesize, descriptor, column, row, 3, count, 0, sizeof(uint16_t));
            for (int index = 0; index < count; ++index)
            {
                if (samples[index] != maximum) { return false; }
            }
        }
    }
    return true;
}
bool IsHardwareSetupFailure(int result) noexcept
{ return result == AVERROR(ENOSYS) || result == AVERROR(ENODEV) || result == AVERROR(ENOTSUP); }
}
