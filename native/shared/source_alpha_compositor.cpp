#include "source_alpha_compositor.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <string>
extern "C"
{
#include <libavutil/csp.h>
#include <libavutil/error.h>
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
#include <libswscale/swscale.h>
}

namespace aeginext::media
{
namespace
{
void Check(int result, const char *operation)
{
    if (result < 0)
    {
        char message[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, message, sizeof(message));
        throw CoreError(ErrorCode::NativeFailure, std::string(operation) + ": " + message);
    }
}
uint16_t Quantize(double value)
{
    if (!std::isfinite(value))
    {
        throw CoreError(ErrorCode::Unsupported, "Source alpha composition produced a non-finite color sample.");
    }
    return static_cast<uint16_t>(std::clamp(std::llround(value), 0LL, 65535LL));
}
SwsContext *MakeNormalizer(const AVFrame *source, AVPixelFormat format, int threads)
{
    auto *context = sws_alloc_context();
    if (!context)
    {
        throw std::bad_alloc();
    }
    try
    {
        Check(av_opt_set_int(context, "srcw", source->width, 0), "alpha source width");
        Check(av_opt_set_int(context, "srch", source->height, 0), "alpha source height");
        Check(av_opt_set_int(context, "src_format", source->format, 0), "alpha source format");
        Check(av_opt_set_int(context, "dstw", source->width, 0), "alpha output width");
        Check(av_opt_set_int(context, "dsth", source->height, 0), "alpha output height");
        Check(av_opt_set_int(context, "dst_format", format, 0), "alpha output format");
        const auto fullRange = source->color_range == AVCOL_RANGE_JPEG;
        Check(av_opt_set_int(context, "src_range", fullRange, 0), "alpha source range");
        Check(av_opt_set_int(context, "dst_range", fullRange, 0), "alpha output range");
        Check(av_opt_set_int(context, "sws_flags", SWS_BILINEAR | SWS_ACCURATE_RND | SWS_BITEXACT, 0), "alpha numeric scale flags");
        Check(av_opt_set_int(context, "threads", threads, 0), "alpha numeric scale threads");
        if (source->chroma_location != AVCHROMA_LOC_UNSPECIFIED)
        {
            int x = 0;
            int y = 0;
            Check(av_chroma_location_enum_to_pos(&x, &y, source->chroma_location), "alpha source chroma position");
            Check(av_opt_set_int(context, "src_h_chr_pos", x, 0), "alpha source chroma X");
            Check(av_opt_set_int(context, "src_v_chr_pos", y, 0), "alpha source chroma Y");
        }
        Check(sws_init_context(context, nullptr, nullptr), "initialize source alpha numeric normalization");
        return context;
    }
    catch (...)
    {
        sws_free_context(&context);
        throw;
    }
}
}

SourceAlphaCompositor::SourceAlphaCompositor(int threads) : threads_(std::clamp(threads, 1, 4))
{
}
SourceAlphaCompositor::~SourceAlphaCompositor()
{
    sws_free_context(&normalize_);
}
AVPixelFormat SourceAlphaCompositor::OutputFormat(const AVFrame *source)
{
    const auto format = static_cast<AVPixelFormat>(source->format);
    const auto *descriptor = av_pix_fmt_desc_get(format);
    if (!descriptor || !(descriptor->flags & AV_PIX_FMT_FLAG_ALPHA))
    {
        return format;
    }
    return descriptor->flags & AV_PIX_FMT_FLAG_RGB ? AV_PIX_FMT_GBRP16LE : AV_PIX_FMT_YUV444P16LE;
}
const AVFrame *SourceAlphaCompositor::Composite(const AVFrame *source, const std::function<void()> &checkCancel)
{
    const auto sourceFormat = static_cast<AVPixelFormat>(source->format);
    const auto *descriptor = av_pix_fmt_desc_get(sourceFormat);
    if (!descriptor || !(descriptor->flags & AV_PIX_FMT_FLAG_ALPHA))
    {
        return source;
    }
    if (descriptor->nb_components != 4 || source->alpha_mode < AVALPHA_MODE_UNSPECIFIED ||
        source->alpha_mode >= AVALPHA_MODE_NB)
    {
        throw CoreError(ErrorCode::Unsupported, "Source alpha requires four color and opacity components and a supported association.");
    }
    for (int component = 0; component < 4; ++component)
    {
        if (descriptor->comp[component].depth < 8 || descriptor->comp[component].depth > 16)
        {
            throw CoreError(ErrorCode::Unsupported, "Source alpha component depths must be between eight and sixteen bits.");
        }
    }
    const auto decode = av_csp_itu_eotf(source->color_trc);
    const auto encode = av_csp_itu_eotf_inv(source->color_trc);
    if (!decode || !encode)
    {
        throw CoreError(ErrorCode::Unsupported, "Source alpha requires a supported official source transfer function.");
    }
    const auto rgb = (descriptor->flags & AV_PIX_FMT_FLAG_RGB) != 0;
    const auto *coefficients = rgb ? nullptr : av_csp_luma_coeffs_from_avcsp(source->colorspace);
    if (!rgb && !coefficients)
    {
        throw CoreError(ErrorCode::Unsupported, "Source alpha requires a supported non-constant-luminance YUV matrix.");
    }
    const auto format = OutputFormat(source);
    if (!output_)
    {
        output_.reset(av_frame_alloc());
        if (!output_)
        {
            throw std::bad_alloc();
        }
    }
    av_frame_unref(output_.get());
    if (!pixels_ || pixels_->format != format || pixels_->width != source->width || pixels_->height != source->height)
    {
        pixels_.reset(av_frame_alloc());
        if (!pixels_)
        {
            throw std::bad_alloc();
        }
        pixels_->format = format;
        pixels_->width = source->width;
        pixels_->height = source->height;
        Check(av_frame_get_buffer(pixels_.get(), 32), "allocate source alpha color buffer");
    }
    Check(av_frame_make_writable(pixels_.get()), "make source alpha color buffer writable");
    Check(av_frame_ref(output_.get(), pixels_.get()), "reference source alpha color buffer");
    Check(av_frame_copy_props(output_.get(), source), "copy source alpha color properties");
    output_->alpha_mode = AVALPHA_MODE_UNSPECIFIED;
    if (!normalize_ || sourceFormat_ != sourceFormat || width_ != source->width || height_ != source->height ||
        range_ != source->color_range || chroma_ != source->chroma_location)
    {
        auto *context = MakeNormalizer(source, format, threads_);
        sws_free_context(&normalize_);
        normalize_ = context;
        sourceFormat_ = sourceFormat;
        width_ = source->width;
        height_ = source->height;
        range_ = source->color_range;
        chroma_ = source->chroma_location;
    }
    const auto rows = sws_scale(normalize_, source->data, source->linesize, 0, source->height,
        output_->data, output_->linesize);
    if (rows != source->height)
    {
        throw CoreError(ErrorCode::NativeFailure, "Source alpha normalization did not produce the full coded frame.");
    }
    alpha_.resize(source->width);
    const auto alphaMaximum = static_cast<double>((uint32_t{1} << descriptor->comp[3].depth) - 1);
    const auto white = source->color_trc == AVCOL_TRC_SMPTE2084 ? 10000.0 :
        source->color_trc == AVCOL_TRC_ARIB_STD_B67 ? 1000.0 : 203.0;
    const auto limited = source->color_range == AVCOL_RANGE_MPEG;
    const auto yOffset = limited ? 4096.0 : 0.0;
    const auto yScale = limited ? 56064.0 : 65535.0;
    const auto uvScale = limited ? 57344.0 : 65535.0;
    const auto kr = rgb ? 0.0 : av_q2d(coefficients->cr);
    const auto kg = rgb ? 0.0 : av_q2d(coefficients->cg);
    const auto kb = rgb ? 0.0 : av_q2d(coefficients->cb);
    const uint8_t *sourceData[4]{source->data[0], source->data[1], source->data[2], source->data[3]};
    for (int y = 0; y < source->height; ++y)
    {
        if (checkCancel)
        {
            checkCancel();
        }
        av_read_image_line2(alpha_.data(), sourceData, source->linesize, descriptor, 0, y, 3, source->width, 0, 4);
        uint16_t *planes[3]{};
        for (int plane = 0; plane < 3; ++plane)
        {
            planes[plane] = reinterpret_cast<uint16_t *>(output_->data[plane] + y * output_->linesize[plane]);
        }
        for (int x = 0; x < source->width; ++x)
        {
            const auto alpha = alpha_[x] / alphaMaximum;
            if (alpha == 1)
            {
                continue;
            }
            std::array<double, 3> color{};
            if (alpha != 0)
            {
                if (rgb)
                {
                    color = {planes[2][x] / 65535.0, planes[0][x] / 65535.0, planes[1][x] / 65535.0};
                }
                else
                {
                    const auto luma = (planes[0][x] - yOffset) / yScale;
                    const auto u = (planes[1][x] - 32768.0) / uvScale;
                    const auto v = (planes[2][x] - 32768.0) / uvScale;
                    color = {luma + 2 * (1 - kr) * v,
                        luma - 2 * kb * (1 - kb) / kg * u - 2 * kr * (1 - kr) / kg * v,
                        luma + 2 * (1 - kb) * u};
                }
                if (source->alpha_mode == AVALPHA_MODE_PREMULTIPLIED)
                {
                    for (auto &component : color)
                    {
                        component /= alpha;
                    }
                }
                if (source->color_trc == AVCOL_TRC_SMPTE2084)
                {
                    for (auto &component : color)
                    {
                        component = std::clamp(component, 0.0, 1.0);
                    }
                }
                decode(white, 0, color.data());
                for (auto &component : color)
                {
                    component *= alpha;
                }
                encode(white, 0, color.data());
            }
            if (rgb)
            {
                planes[2][x] = Quantize(color[0] * 65535);
                planes[0][x] = Quantize(color[1] * 65535);
                planes[1][x] = Quantize(color[2] * 65535);
            }
            else
            {
                const auto luma = kr * color[0] + kg * color[1] + kb * color[2];
                planes[0][x] = Quantize(luma * yScale + yOffset);
                planes[1][x] = Quantize((color[2] - luma) / (2 * (1 - kb)) * uvScale + 32768);
                planes[2][x] = Quantize((color[0] - luma) / (2 * (1 - kr)) * uvScale + 32768);
            }
        }
    }
    return output_.get();
}
}
