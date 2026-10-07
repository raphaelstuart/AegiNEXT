#include "legacy_yuv_frame_pipeline.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <stdexcept>
#include <string>

extern "C"
{
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
}

namespace aeginext::encode::tests
{
namespace
{
void Check(int result, const char *operation)
{
    if (result < 0)
    {
        char text[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, text, sizeof(text));
        throw std::runtime_error(std::string(operation) + ": " + text);
    }
}

AVFrame *Allocate(int width, int height)
{
    auto *frame = av_frame_alloc();
    if (!frame)
    {
        throw std::bad_alloc();
    }
    frame->format = AV_PIX_FMT_YUV444P16LE;
    frame->width = width;
    frame->height = height;
    try
    {
        Check(av_frame_get_buffer(frame, 32), "legacy frame buffer");
        return frame;
    }
    catch (...)
    {
        av_frame_free(&frame);
        throw;
    }
}

SwsContext *MakeScaler(int sourceWidth, int sourceHeight, AVPixelFormat sourceFormat,
    int destinationWidth, int destinationHeight, AVPixelFormat destinationFormat,
    AVChromaLocation sourceLocation, AVChromaLocation destinationLocation, bool fullRange)
{
    auto *context = sws_alloc_context();
    if (!context)
    {
        throw std::bad_alloc();
    }
    try
    {
        Check(av_opt_set_int(context, "srcw", sourceWidth, 0), "legacy srcw");
        Check(av_opt_set_int(context, "srch", sourceHeight, 0), "legacy srch");
        Check(av_opt_set_int(context, "src_format", sourceFormat, 0), "legacy src format");
        Check(av_opt_set_int(context, "dstw", destinationWidth, 0), "legacy dstw");
        Check(av_opt_set_int(context, "dsth", destinationHeight, 0), "legacy dsth");
        Check(av_opt_set_int(context, "dst_format", destinationFormat, 0), "legacy dst format");
        Check(av_opt_set_int(context, "src_range", fullRange, 0), "legacy source range");
        Check(av_opt_set_int(context, "dst_range", fullRange, 0), "legacy target range");
        Check(av_opt_set_int(context, "sws_flags", SWS_BILINEAR | SWS_ACCURATE_RND | SWS_BITEXACT, 0), "legacy scale flags");
        for (int side = 0; side < 2; ++side)
        {
            const auto location = side ? destinationLocation : sourceLocation;
            if (location == AVCHROMA_LOC_UNSPECIFIED)
            {
                continue;
            }
            int x = 0;
            int y = 0;
            Check(av_chroma_location_enum_to_pos(&x, &y, location), "legacy chroma position");
            Check(av_opt_set_int(context, side ? "dst_h_chr_pos" : "src_h_chr_pos", x, 0), "legacy chroma X");
            Check(av_opt_set_int(context, side ? "dst_v_chr_pos" : "src_v_chr_pos", y, 0), "legacy chroma Y");
        }
        Check(sws_init_context(context, nullptr, nullptr), "legacy initialize same-domain YUV resampling");
        return context;
    }
    catch (...)
    {
        sws_free_context(&context);
        throw;
    }
}
}

LegacyYuvFramePipeline::LegacyYuvFramePipeline(const AVFrame *source, int width, int height, AVPixelFormat outputFormat)
    : width_(width), height_(height)
{
    try
    {
        upsampled_ = Allocate(source->width, source->height);
        composed_ = Allocate(width, height);
        upsample_ = MakeScaler(source->width, source->height, static_cast<AVPixelFormat>(source->format),
            source->width, source->height, AV_PIX_FMT_YUV444P16LE,
            source->chroma_location, AVCHROMA_LOC_UNSPECIFIED, source->color_range == AVCOL_RANGE_JPEG);
        downsample_ = MakeScaler(width, height, AV_PIX_FMT_YUV444P16LE, width, height,
            outputFormat, AVCHROMA_LOC_UNSPECIFIED, AVCHROMA_LOC_LEFT, source->color_range == AVCOL_RANGE_JPEG);
    }
    catch (...)
    {
        Release();
        throw;
    }
}

LegacyYuvFramePipeline::~LegacyYuvFramePipeline()
{
    Release();
}

void LegacyYuvFramePipeline::Release() noexcept
{
    sws_free_context(&upsample_);
    sws_free_context(&downsample_);
    av_frame_free(&upsampled_);
    av_frame_free(&composed_);
}

int LegacyYuvFramePipeline::Convert(const AVFrame *source, std::span<const float> layer,
    const ColorPipeline &color, double referenceWhite, AVFrame *output)
{
    const auto scaled = sws_scale(upsample_, source->data, source->linesize, 0, source->height,
        upsampled_->data, upsampled_->linesize);
    if (scaled < 0)
    {
        return scaled;
    }
    for (int y = 0; y < height_; ++y)
    {
        const uint16_t *input[3]{};
        uint16_t *destination[3]{};
        for (int plane = 0; plane < 3; ++plane)
        {
            input[plane] = reinterpret_cast<uint16_t *>(upsampled_->data[plane] +
                (y + source->crop_top) * upsampled_->linesize[plane]) + source->crop_left;
            destination[plane] = reinterpret_cast<uint16_t *>(composed_->data[plane] + y * composed_->linesize[plane]);
            std::memcpy(destination[plane], input[plane], width_ * 2);
        }
        for (int x = 0; x < width_; ++x)
        {
            const auto *pixel = &layer[(static_cast<size_t>(y) * width_ + x) * 4];
            if (pixel[3] == 0)
            {
                continue;
            }
            const auto limited = source->color_range == AVCOL_RANGE_MPEG;
            const double yOffset = limited ? 4096 : 0;
            const double yScale = limited ? 56064 : 65535;
            const double uvScale = limited ? 57344 : 65535;
            const auto encoded = color.Composite(
                {(input[0][x] - yOffset) / yScale, (input[1][x] - 32768.0) / uvScale, (input[2][x] - 32768.0) / uvScale},
                {pixel[0], pixel[1], pixel[2], pixel[3]}, referenceWhite);
            for (int plane = 0; plane < 3; ++plane)
            {
                const auto value = encoded[plane] * (plane == 0 ? yScale : uvScale) + (plane == 0 ? yOffset : 32768);
                if (!std::isfinite(value))
                {
                    throw std::runtime_error("Non-finite legacy composited sample");
                }
                destination[plane][x] = static_cast<uint16_t>(std::clamp(std::llround(value), 0LL, 65535LL));
            }
        }
    }
    return sws_scale(downsample_, composed_->data, composed_->linesize, 0, height_, output->data, output->linesize);
}
}
