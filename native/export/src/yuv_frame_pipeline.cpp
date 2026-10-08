#include "yuv_frame_pipeline.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <stdexcept>
extern "C"
{
#include <libavutil/error.h>
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
#include <libswscale/swscale.h>
}

namespace aeginext::encode
{
namespace
{
void Check(int result, const char *operation)
{
    if (result < 0)
    {
        char text[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, text, sizeof(text));
        throw aeginext::media::CoreError(aeginext::media::ErrorCode::NativeFailure,
            std::string(operation) + ": " + text);
    }
}

SwsContext *MakeScaler(int sourceWidth, int sourceHeight, AVPixelFormat sourceFormat,
    int width, int height, AVPixelFormat outputFormat, AVChromaLocation sourceLocation,
    AVChromaLocation outputLocation, bool fullRange, int threads)
{
    auto *context = sws_alloc_context();
    if (!context)
    {
        throw std::bad_alloc();
    }

    try
    {
        Check(av_opt_set_int(context, "srcw", sourceWidth, 0), "srcw");
        Check(av_opt_set_int(context, "srch", sourceHeight, 0), "srch");
        Check(av_opt_set_int(context, "src_format", sourceFormat, 0), "src format");
        Check(av_opt_set_int(context, "dstw", width, 0), "dstw");
        Check(av_opt_set_int(context, "dsth", height, 0), "dsth");
        Check(av_opt_set_int(context, "dst_format", outputFormat, 0), "dst format");
        Check(av_opt_set_int(context, "src_range", fullRange, 0), "source range");
        Check(av_opt_set_int(context, "dst_range", fullRange, 0), "target range");
        Check(av_opt_set_int(context, "sws_flags", SWS_BILINEAR | SWS_ACCURATE_RND | SWS_BITEXACT, 0), "scale flags");
        Check(av_opt_set_int(context, "threads", std::clamp(threads, 1, 4), 0), "scale threads");
        for (auto side = 0; side < 2; ++side)
        {
            const auto location = side ? outputLocation : sourceLocation;
            if (location == AVCHROMA_LOC_UNSPECIFIED)
            {
                continue;
            }

            auto x = 0;
            auto y = 0;
            Check(av_chroma_location_enum_to_pos(&x, &y, location), "chroma position");
            Check(av_opt_set_int(context, side ? "dst_h_chr_pos" : "src_h_chr_pos", x, 0), "chroma X");
            Check(av_opt_set_int(context, side ? "dst_v_chr_pos" : "src_v_chr_pos", y, 0), "chroma Y");
        }

        Check(sws_init_context(context, nullptr, nullptr), "initialize same-domain YUV resampling");
        return context;
    }
    catch (...)
    {
        sws_free_context(&context);
        throw;
    }
}
}

YuvFramePipeline::YuvFramePipeline(const AVFrame *source, int width, int height,
    AVPixelFormat outputFormat, int threads)
    : width_(width), height_(height), sourceFormat_(static_cast<AVPixelFormat>(source->format)),
      outputFormat_(outputFormat), upsampled_(av_frame_alloc()), visible_(av_frame_alloc()), sourceAlpha_(threads),
      rows_(static_cast<size_t>(std::clamp(threads, 1, 4)))
{
    if (!upsampled_ || !visible_)
    {
        throw std::bad_alloc();
    }

    upsampled_->format = AV_PIX_FMT_YUV444P16LE;
    upsampled_->width = source->width;
    upsampled_->height = source->height;
    Check(av_frame_get_buffer(upsampled_.get(), 32), "frame buffer");
    try
    {
        const auto fullRange = source->color_range == AVCOL_RANGE_JPEG;
        upsample_ = MakeScaler(source->width, source->height, aeginext::media::SourceAlphaCompositor::OutputFormat(source), source->width, source->height,
            AV_PIX_FMT_YUV444P16LE, source->chroma_location, AVCHROMA_LOC_UNSPECIFIED, fullRange, threads);
        downsample_ = MakeScaler(width_, height_, AV_PIX_FMT_YUV444P16LE, width_, height_, outputFormat_,
            AVCHROMA_LOC_UNSPECIFIED, AVCHROMA_LOC_LEFT, fullRange, threads);
    }
    catch (...)
    {
        sws_free_context(&upsample_);
        sws_free_context(&downsample_);
        throw;
    }
}

YuvFramePipeline::~YuvFramePipeline()
{
    sws_free_context(&upsample_);
    sws_free_context(&downsample_);
}

int YuvFramePipeline::Upsample(const AVFrame *source, const std::function<void()> &checkCancel)
{
    ready_ = false;
    av_frame_unref(visible_.get());
    if (source->format != sourceFormat_ || source->width != upsampled_->width ||
        source->height != upsampled_->height || source->crop_left >= static_cast<size_t>(source->width) ||
        source->crop_right >= static_cast<size_t>(source->width) - source->crop_left ||
        source->crop_top >= static_cast<size_t>(source->height) ||
        source->crop_bottom >= static_cast<size_t>(source->height) - source->crop_top ||
        static_cast<size_t>(width_) != static_cast<size_t>(source->width) - source->crop_left - source->crop_right ||
        static_cast<size_t>(height_) != static_cast<size_t>(source->height) - source->crop_top - source->crop_bottom)
    {
        return AVERROR(EINVAL);
    }

    auto result = av_frame_make_writable(upsampled_.get());
    if (result < 0)
    {
        return result;
    }

    result = sws_scale_frame(upsample_, upsampled_.get(), sourceAlpha_.Composite(source, checkCancel));
    if (result < 0)
    {
        return result;
    }

    result = av_frame_ref(visible_.get(), upsampled_.get());
    if (result < 0)
    {
        return result;
    }

    for (auto plane = 0; plane < 3; ++plane)
    {
        visible_->data[plane] += source->crop_top * visible_->linesize[plane] + source->crop_left * sizeof(uint16_t);
    }
    visible_->width = width_;
    visible_->height = height_;
    visible_->crop_left = 0;
    visible_->crop_right = 0;
    visible_->crop_top = 0;
    visible_->crop_bottom = 0;
    ready_ = true;
    return 0;
}

void YuvFramePipeline::Composite(const AVFrame *source, std::span<const float> layer,
    const ColorPipeline &color, double referenceWhite, const std::function<void()> &checkCancel)
{
    if (!ready_ || layer.size() < static_cast<size_t>(width_) * height_ * 4)
    {
        ready_ = false;
        throw std::invalid_argument("YUV composition requires an upsampled frame and a complete subtitle layer");
    }

    try
    {
        const auto limited = source->color_range == AVCOL_RANGE_MPEG;
        const double yOffset = limited ? 4096 : 0;
        const double yScale = limited ? 56064 : 65535;
        const double uvScale = limited ? 57344 : 65535;
        rows_.Execute(static_cast<size_t>(height_), [&](size_t begin, size_t end)
        {
            for (auto y = begin; y < end; ++y)
            {
                if (!(y % 32))
                {
                    checkCancel();
                }

                uint16_t *planes[3]{};
                for (auto plane = 0; plane < 3; ++plane)
                {
                    planes[plane] = reinterpret_cast<uint16_t *>(visible_->data[plane] + y * visible_->linesize[plane]);
                }

                for (auto x = 0; x < width_; ++x)
                {
                    const auto *pixel = &layer[(y * width_ + x) * 4];
                    if (pixel[3] == 0)
                    {
                        continue;
                    }

                    const auto encoded = color.Composite({(planes[0][x] - yOffset) / yScale,
                        (planes[1][x] - 32768.0) / uvScale, (planes[2][x] - 32768.0) / uvScale},
                        {pixel[0], pixel[1], pixel[2], pixel[3]}, referenceWhite);
                    for (auto plane = 0; plane < 3; ++plane)
                    {
                        const auto value = encoded[plane] * (plane == 0 ? yScale : uvScale) +
                            (plane == 0 ? yOffset : 32768);
                        if (!std::isfinite(value))
                        {
                            throw aeginext::media::CoreError(aeginext::media::ErrorCode::Unsupported,
                                "Non-finite composited sample");
                        }

                        planes[plane][x] = static_cast<uint16_t>(std::clamp(std::llround(value), 0LL, 65535LL));
                    }
                }
            }
        });
    }
    catch (...)
    {
        ready_ = false;
        throw;
    }
}

void YuvFramePipeline::Composite(const AVFrame *source, PreparedOverlay &overlay,
    const ColorPipeline &color, double referenceWhite, const std::function<void()> &checkCancel)
{
    if (!ready_ || overlay.Width() != static_cast<uint32_t>(width_) ||
        overlay.Height() != static_cast<uint32_t>(height_) || !overlay.IsCompatible(color, referenceWhite))
    {
        ready_ = false;
        overlay.Invalidate();
        throw std::invalid_argument("YUV composition requires a compatible prepared overlay");
    }

    try
    {
        checkCancel();
        if (!overlay.CoverageKnown())
        {
            Composite(source, overlay.Pixels(), color, referenceWhite, checkCancel);
            return;
        }
        if (!overlay.ActivePixels())
        {
            return;
        }
        const auto limited = source->color_range == AVCOL_RANGE_MPEG;
        const double yOffset = limited ? 4096 : 0;
        const double yScale = limited ? 56064 : 65535;
        const double uvScale = limited ? 57344 : 65535;
        const auto pixels = overlay.Pixels();
        rows_.Execute(static_cast<size_t>(height_), [&](size_t begin, size_t end)
        {
            for (auto y = begin; y < end; ++y)
            {
                if (!(y % 32))
                {
                    checkCancel();
                }
                const auto &row = overlay.Row(y);
                if (!row.end)
                {
                    continue;
                }
                uint16_t *planes[3]{};
                for (auto plane = 0; plane < 3; ++plane)
                {
                    planes[plane] = reinterpret_cast<uint16_t *>(visible_->data[plane] + y * visible_->linesize[plane]);
                }
                const auto write = [&](uint32_t x, const Color &encoded)
                {
                    for (auto plane = 0; plane < 3; ++plane)
                    {
                        const auto value = encoded[plane] * (plane == 0 ? yScale : uvScale) +
                            (plane == 0 ? yOffset : 32768);
                        if (!std::isfinite(value))
                        {
                            throw aeginext::media::CoreError(aeginext::media::ErrorCode::Unsupported,
                                "Non-finite composited sample");
                        }
                        planes[plane][x] = static_cast<uint16_t>(std::clamp(std::llround(value), 0LL, 65535LL));
                    }
                };
                if (row.runCount)
                {
                    for (const auto &run : overlay.Runs(row))
                    {
                        const auto &foreground = overlay.Foreground(run.color);
                        for (auto x = run.begin; x < run.end; ++x)
                        {
                            write(x, color.CompositePrepared({(planes[0][x] - yOffset) / yScale,
                                (planes[1][x] - 32768.0) / uvScale, (planes[2][x] - 32768.0) / uvScale},
                                foreground, referenceWhite));
                        }
                    }
                }
                else
                {
                    for (auto x = row.begin; x < row.end; ++x)
                    {
                        const auto *pixel = &pixels[(y * width_ + x) * 4];
                        if (pixel[3] == 0)
                        {
                            continue;
                        }
                        write(x, color.Composite({(planes[0][x] - yOffset) / yScale,
                            (planes[1][x] - 32768.0) / uvScale, (planes[2][x] - 32768.0) / uvScale},
                            {pixel[0], pixel[1], pixel[2], pixel[3]}, referenceWhite));
                    }
                }
            }
        });
    }
    catch (...)
    {
        ready_ = false;
        overlay.Invalidate();
        throw;
    }
}

int YuvFramePipeline::Downsample(AVFrame *output)
{
    if (!ready_ || output->format != outputFormat_ || output->width != width_ || output->height != height_)
    {
        return AVERROR(EINVAL);
    }

    return sws_scale_frame(downsample_, output, visible_.get());
}
}
