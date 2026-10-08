#include "preview_converter.h"
#include "decode_versions.h"
#include "color_resolution.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>

extern "C"
{
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/cpu.h>
}

namespace aeginext::decode
{
namespace
{
constexpr uint64_t MAX_CODED_PIXELS = 33177600;
constexpr uint64_t MAX_OUTPUT_PIXELS = 16777216;

bool SupportedSideData(AVFrameSideDataType type)
{
    switch (type)
    {
    case AV_FRAME_DATA_DISPLAYMATRIX:
    case AV_FRAME_DATA_STEREO3D:
    case AV_FRAME_DATA_DYNAMIC_HDR_PLUS:
    case AV_FRAME_DATA_DOVI_RPU_BUFFER:
    case AV_FRAME_DATA_DOVI_METADATA:
    case AV_FRAME_DATA_DYNAMIC_HDR_VIVID:
    case AV_FRAME_DATA_DYNAMIC_HDR_SMPTE_2094_APP5:
    case AV_FRAME_DATA_ICC_PROFILE:
    case AV_FRAME_DATA_RAW_COLOR_PARAMS:
    case AV_FRAME_DATA_FILM_GRAIN_PARAMS:
    case AV_FRAME_DATA_AMBIENT_VIEWING_ENVIRONMENT:
        return false;
    default:
        return true;
    }
}

double Rational(AVRational value)
{
    if (value.den <= 0)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Mastering metadata has an invalid rational denominator.");
    }
    return static_cast<double>(value.num) / value.den;
}

void ValidateMastering(const AVFrame *frame)
{
    const auto *data = av_frame_get_side_data(frame, AV_FRAME_DATA_MASTERING_DISPLAY_METADATA);
    if (!data)
    {
        return;
    }
    if (data->size < sizeof(AVMasteringDisplayMetadata))
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Mastering metadata is truncated.");
    }
    const auto *metadata = reinterpret_cast<const AVMasteringDisplayMetadata *>(data->data);
    if (metadata->has_luminance)
    {
        const auto minimum = Rational(metadata->min_luminance);
        const auto maximum = Rational(metadata->max_luminance);
        if (minimum < 0 || maximum <= minimum || maximum > 10000)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Mastering luminance must satisfy 0 <= minimum < maximum <= 10000 nits.");
        }
    }
    if (metadata->has_primaries)
    {
        double xy[3][2]{};
        for (size_t index = 0; index < 3; ++index)
        {
            xy[index][0] = Rational(metadata->display_primaries[index][0]);
            xy[index][1] = Rational(metadata->display_primaries[index][1]);
            if (xy[index][0] < 0 || xy[index][1] <= 0 || xy[index][0] + xy[index][1] > 1)
            {
                throw Error(AN_DECODE_UNSUPPORTED, "Mastering primaries are outside the chromaticity domain.");
            }
        }
        const auto area = (xy[1][0] - xy[0][0]) * (xy[2][1] - xy[0][1]) -
            (xy[2][0] - xy[0][0]) * (xy[1][1] - xy[0][1]);
        const auto whiteX = Rational(metadata->white_point[0]);
        const auto whiteY = Rational(metadata->white_point[1]);
        if (std::abs(area) <= 1e-10 || whiteX <= 0 || whiteY <= 0 || whiteX + whiteY > 1)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Mastering gamut is degenerate or its white point is invalid.");
        }
    }
}

void ValidateSource(const AVFrame *frame, const an_preview_request &request)
{
    const auto format = static_cast<AVPixelFormat>(frame->format);
    const auto *descriptor = av_pix_fmt_desc_get(format);
    constexpr auto rejectedFlags = AV_PIX_FMT_FLAG_FLOAT | AV_PIX_FMT_FLAG_HWACCEL |
        AV_PIX_FMT_FLAG_PAL | AV_PIX_FMT_FLAG_BAYER |
        AV_PIX_FMT_FLAG_BITSTREAM | AV_PIX_FMT_FLAG_XYZ;
    if (!descriptor || descriptor->nb_components != ((descriptor->flags & AV_PIX_FMT_FLAG_ALPHA) ? 4 : 3) ||
        (descriptor->flags & rejectedFlags) != 0 ||
        frame->hw_frames_ctx || !sws_test_format(format, 0))
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Preview requires supported integer RGB or YUV with optional alpha.");
    }
    for (int component = 0; component < descriptor->nb_components; ++component)
    {
        if (descriptor->comp[component].depth < 8 || descriptor->comp[component].depth > 16)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Preview component depths must be between 8 and 16 bits.");
        }
    }
    if ((frame->flags & AV_FRAME_FLAG_INTERLACED) != 0)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Interlaced preview requires a separate deinterlacing policy.");
    }
    if ((frame->flags & AV_FRAME_FLAG_CORRUPT) != 0 || frame->decode_error_flags != 0)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Corrupt or partially decoded frames cannot be used for color-managed preview.");
    }
    if ((frame->color_range != AVCOL_RANGE_MPEG && frame->color_range != AVCOL_RANGE_JPEG) ||
        (frame->color_primaries != AVCOL_PRI_BT709 && frame->color_primaries != AVCOL_PRI_BT470BG && frame->color_primaries != AVCOL_PRI_SMPTE170M && frame->color_primaries != AVCOL_PRI_BT2020) ||
        (frame->color_trc != AVCOL_TRC_BT709 && frame->color_trc != AVCOL_TRC_SMPTE170M && frame->color_trc != AVCOL_TRC_IEC61966_2_1 &&
            frame->color_trc != AVCOL_TRC_SMPTE2084 && frame->color_trc != AVCOL_TRC_ARIB_STD_B67))
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Preview source range, primaries and transfer must be explicit and supported.");
    }
    const auto rgb = (descriptor->flags & AV_PIX_FMT_FLAG_RGB) != 0;
    const auto jpegFormat = format == AV_PIX_FMT_YUVJ420P || format == AV_PIX_FMT_YUVJ422P ||
        format == AV_PIX_FMT_YUVJ444P || format == AV_PIX_FMT_YUVJ440P || format == AV_PIX_FMT_YUVJ411P;
    if (jpegFormat && frame->color_range != AVCOL_RANGE_JPEG)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "JPEG YUV pixel formats conflict with non-full-range frame metadata.");
    }
    if (rgb)
    {
        if (frame->colorspace != AVCOL_SPC_RGB || frame->color_range != AVCOL_RANGE_JPEG)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Integer RGB preview requires explicit RGB matrix and full range.");
        }
    }
    else if (frame->colorspace != AVCOL_SPC_BT709 && frame->colorspace != AVCOL_SPC_BT470BG &&
        frame->colorspace != AVCOL_SPC_SMPTE170M && frame->colorspace != AVCOL_SPC_BT2020_NCL)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Preview source YUV matrix is unspecified or unsupported.");
    }
    if (frame->chroma_location < AVCHROMA_LOC_UNSPECIFIED || frame->chroma_location >= AVCHROMA_LOC_NB ||
        ((descriptor->log2_chroma_w || descriptor->log2_chroma_h) && frame->chroma_location == AVCHROMA_LOC_UNSPECIFIED))
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Subsampled preview requires an explicit supported chroma location.");
    }
    if (request.color_range != frame->color_range || request.color_matrix != frame->colorspace ||
        request.color_primaries != frame->color_primaries || request.color_transfer != frame->color_trc ||
        request.chroma_location != frame->chroma_location || request.alpha_mode != frame->alpha_mode)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "Preview request does not match the effective frame color metadata; overrides are unsupported.");
    }
    for (int index = 0; index < frame->nb_side_data; ++index)
    {
        if (!SupportedSideData(frame->side_data[index]->type))
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Preview source has unsupported display, dynamic HDR or auxiliary color metadata.");
        }
    }
    if (frame->color_trc == AVCOL_TRC_SMPTE2084)
    {
        ValidateMastering(frame);
    }
}

FramePointer MakeBgraFrame(int width, int height)
{
    FramePointer frame(av_frame_alloc());
    if (!frame)
    {
        throw std::bad_alloc();
    }
    frame->format = AV_PIX_FMT_BGRA;
    frame->width = width;
    frame->height = height;
    frame->colorspace = AVCOL_SPC_RGB;
    frame->color_range = AVCOL_RANGE_JPEG;
    frame->color_primaries = AVCOL_PRI_BT709;
    frame->color_trc = AVCOL_TRC_IEC61966_2_1;
    frame->alpha_mode = AVALPHA_MODE_STRAIGHT;
    frame->sample_aspect_ratio = {1, 1};
    CheckAv(av_frame_get_buffer(frame.get(), 32), AN_DECODE_NATIVE_FAILURE, "av_frame_get_buffer(preview)");
    return frame;
}
}

an_preview_backend_info PreviewConverter::BackendInfo()
{
    static_assert(LIBSWSCALE_VERSION_INT == AN_EXPECTED_SWSCALE);
    ValidateBackend();
    const auto runtime = swscale_version();
    if (runtime != AN_EXPECTED_SWSCALE)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Preview swscale runtime does not match the pinned development headers.");
    }
    return {sizeof(an_preview_backend_info), AN_DECODE_ABI_VERSION, LIBSWSCALE_VERSION_INT, runtime};
}

PreviewConverter::PreviewConverter() : PreviewConverter(std::clamp(av_cpu_count(), 1, 4))
{
}

PreviewConverter::PreviewConverter(int threadBudget) : sourceAlpha_(threadBudget)
{
    if (threadBudget < 1 || threadBudget > 4)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "CMS thread budget must be between one and four.");
    }
    BackendInfo();
    cms_ = sws_alloc_context();
    if (!cms_)
    {
        throw std::bad_alloc();
    }
    cms_->backends = SWS_BACKEND_LEGACY;
    cms_->intent = SWS_INTENT_PERCEPTUAL;
    cms_->flags = SWS_STRICT | SWS_ACCURATE_RND | SWS_BITEXACT;
    cms_->scaler = SWS_SCALE_BILINEAR;
    cms_->scaler_sub = SWS_SCALE_BILINEAR;
    cms_->threads = threadBudget;
    cms_->dither = SWS_DITHER_BAYER;
    cms_->gamma_flag = 0;
}

PreviewConverter::~PreviewConverter()
{
    sws_free_context(&cms_);
    sws_free_context(&resampler_);
}

void PreviewConverter::Convert(const FrameOwner &owner, const an_preview_request &request,
    uint8_t *destination, uint64_t capacity)
{
    if (request.struct_size != sizeof(request) || request.abi_version != AN_DECODE_ABI_VERSION ||
        request.flags != 0 || request.reserved != 0 || request.width == 0 || request.height == 0 ||
        static_cast<uint64_t>(request.width) * request.height > MAX_OUTPUT_PIXELS)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "Invalid preview request ABI, dimensions, flags or reserved value.");
    }
    const auto outputBytes = static_cast<uint64_t>(request.width) * request.height * 4;
    if (!destination || capacity < outputBytes || outputBytes > std::numeric_limits<size_t>::max())
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "Preview output buffer is missing or smaller than the tight BGRA image.");
    }
    const auto *original = owner.NativeFrame();
    if (static_cast<uint64_t>(original->width) * original->height > MAX_CODED_PIXELS)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Coded preview dimensions exceed the supported pixel limit.");
    }
    const auto resolved = aeginext::media::ResolveColor(original, owner.ColorContext());
    FramePointer source(av_frame_clone(original));
    if (!source)
    {
        throw std::bad_alloc();
    }
    aeginext::media::ApplyColor(source.get(), resolved);
    ValidateSource(source.get(), request);
    if (source->color_trc == AVCOL_TRC_ARIB_STD_B67)
    {
        av_frame_remove_side_data(source.get(), AV_FRAME_DATA_MASTERING_DISPLAY_METADATA);
    }
    source->crop_left = source->crop_top = source->crop_right = source->crop_bottom = 0;
    const auto *opaqueSource = sourceAlpha_.Composite(source.get());
    if (!converted_ || converted_->width != source->width || converted_->height != source->height)
    {
        converted_ = MakeBgraFrame(source->width, source->height);
    }
    CheckAv(av_frame_make_writable(converted_.get()), AN_DECODE_NATIVE_FAILURE, "av_frame_make_writable(preview CMS)");
    CheckAv(sws_frame_setup(cms_, converted_.get(), opaqueSource), AN_DECODE_UNSUPPORTED, "sws_frame_setup(preview CMS)");
    CheckAv(sws_scale_frame(cms_, converted_.get(), opaqueSource), AN_DECODE_NATIVE_FAILURE, "sws_scale_frame(preview CMS)");
    const auto visibleWidth = original->width - static_cast<int>(original->crop_left + original->crop_right);
    const auto visibleHeight = original->height - static_cast<int>(original->crop_top + original->crop_bottom);
    const uint8_t *croppedData[4]{converted_->data[0] + original->crop_top * converted_->linesize[0] + original->crop_left * 4};
    const int croppedStride[4]{converted_->linesize[0]};
    if (request.width == static_cast<uint32_t>(visibleWidth) && request.height == static_cast<uint32_t>(visibleHeight))
    {
        const auto rowBytes = static_cast<size_t>(visibleWidth) * 4;
        for (int row = 0; row < visibleHeight; ++row)
        {
            std::memcpy(destination + row * rowBytes, croppedData[0] + static_cast<ptrdiff_t>(row) * croppedStride[0], rowBytes);
        }
        return;
    }
    if (!output_ || output_->width != static_cast<int>(request.width) || output_->height != static_cast<int>(request.height))
    {
        output_ = MakeBgraFrame(static_cast<int>(request.width), static_cast<int>(request.height));
    }
    resampler_ = sws_getCachedContext(resampler_, visibleWidth, visibleHeight, AV_PIX_FMT_BGRA,
        output_->width, output_->height, AV_PIX_FMT_BGRA, SWS_BILINEAR | SWS_ACCURATE_RND | SWS_BITEXACT,
        nullptr, nullptr, nullptr);
    if (!resampler_)
    {
        throw Error(AN_DECODE_NATIVE_FAILURE, "Cannot initialize the BGRA display resampler.");
    }
    const auto rows = sws_scale(resampler_, croppedData, croppedStride, 0, visibleHeight,
        output_->data, output_->linesize);
    CheckAv(rows, AN_DECODE_NATIVE_FAILURE, "sws_scale(preview display resampler)");
    if (rows != output_->height)
    {
        throw Error(AN_DECODE_NATIVE_FAILURE, "Preview resampling returned an incomplete image.");
    }
    const auto rowBytes = static_cast<size_t>(request.width) * 4;
    for (uint32_t row = 0; row < request.height; ++row)
    {
        std::memcpy(destination + row * rowBytes, output_->data[0] + static_cast<ptrdiff_t>(row) * output_->linesize[0], rowBytes);
    }
}
}
