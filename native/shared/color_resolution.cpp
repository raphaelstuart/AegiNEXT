#include "color_resolution.h"
extern "C"
{
#include <libavutil/pixdesc.h>
}
namespace aeginext::media
{
namespace
{
bool HdrEnums(int matrix, int primaries, int transfer)
{
    return matrix == AVCOL_SPC_BT2020_NCL || matrix == AVCOL_SPC_BT2020_CL ||
        primaries == AVCOL_PRI_BT2020 || transfer == AVCOL_TRC_SMPTE2084 || transfer == AVCOL_TRC_ARIB_STD_B67;
}
bool HdrSideData(AVFrameSideDataType type)
{
    return type == AV_FRAME_DATA_MASTERING_DISPLAY_METADATA || type == AV_FRAME_DATA_CONTENT_LIGHT_LEVEL ||
        type == AV_FRAME_DATA_DYNAMIC_HDR_PLUS || type == AV_FRAME_DATA_DOVI_RPU_BUFFER ||
        type == AV_FRAME_DATA_DOVI_METADATA || type == AV_FRAME_DATA_DYNAMIC_HDR_VIVID ||
        type == AV_FRAME_DATA_DYNAMIC_HDR_SMPTE_2094_APP5;
}
bool JpegFormat(AVPixelFormat format)
{
    return format == AV_PIX_FMT_YUVJ420P || format == AV_PIX_FMT_YUVJ422P || format == AV_PIX_FMT_YUVJ444P ||
        format == AV_PIX_FMT_YUVJ440P || format == AV_PIX_FMT_YUVJ411P;
}
}
bool HasHdrEvidence(const AVFrame *frame)
{
    if (HdrEnums(frame->colorspace, frame->color_primaries, frame->color_trc)) { return true; }
    for (int index = 0; index < frame->nb_side_data; ++index)
    {
        if (HdrSideData(frame->side_data[index]->type)) { return true; }
    }
    return false;
}
bool HasHdrEvidence(const AVCodecParameters *parameters)
{
    if (HdrEnums(parameters->color_space, parameters->color_primaries, parameters->color_trc)) { return true; }
    for (int index = 0; index < parameters->nb_coded_side_data; ++index)
    {
        const auto type = parameters->coded_side_data[index].type;
        if (type == AV_PKT_DATA_MASTERING_DISPLAY_METADATA || type == AV_PKT_DATA_CONTENT_LIGHT_LEVEL ||
            type == AV_PKT_DATA_DOVI_CONF || type == AV_PKT_DATA_DYNAMIC_HDR10_PLUS || type == AV_PKT_DATA_DYNAMIC_HDR_SMPTE_2094_APP5) { return true; }
    }
    return false;
}
bool HasUnsupportedColorMetadata(const AVCodecParameters *parameters)
{
    for (int index = 0; index < parameters->nb_coded_side_data; ++index)
    {
        const auto type = parameters->coded_side_data[index].type;
        if (type == AV_PKT_DATA_DISPLAYMATRIX || type == AV_PKT_DATA_STEREO3D || type == AV_PKT_DATA_ICC_PROFILE ||
            type == AV_PKT_DATA_DOVI_CONF || type == AV_PKT_DATA_DYNAMIC_HDR10_PLUS ||
            type == AV_PKT_DATA_DYNAMIC_HDR_SMPTE_2094_APP5 || type == AV_PKT_DATA_AMBIENT_VIEWING_ENVIRONMENT) { return true; }
    }
    return false;
}
ResolvedColor ResolveColor(const AVFrame *frame, SourceColorContext context)
{
    if (context.unsupportedColorMetadata) { throw CoreError(ErrorCode::Unsupported, "Stream has unsupported display or auxiliary color metadata."); }
    if (!frame) { throw CoreError(ErrorCode::InvalidArgument, "A source frame is required."); }
    if (frame->width <= 0 || frame->height <= 0 || frame->crop_left >= static_cast<size_t>(frame->width) ||
        frame->crop_right >= static_cast<size_t>(frame->width) - frame->crop_left ||
        frame->crop_top >= static_cast<size_t>(frame->height) ||
        frame->crop_bottom >= static_cast<size_t>(frame->height) - frame->crop_top)
    { throw CoreError(ErrorCode::Unsupported, "Color resolution requires a nonempty valid visible crop."); }
    const auto visibleWidth = frame->width - static_cast<int>(frame->crop_left + frame->crop_right);
    const auto visibleHeight = frame->height - static_cast<int>(frame->crop_top + frame->crop_bottom);
    const auto format = static_cast<AVPixelFormat>(frame->format);
    const auto *descriptor = av_pix_fmt_desc_get(format);
    constexpr auto rejected = AV_PIX_FMT_FLAG_FLOAT | AV_PIX_FMT_FLAG_HWACCEL | AV_PIX_FMT_FLAG_ALPHA |
        AV_PIX_FMT_FLAG_PAL | AV_PIX_FMT_FLAG_BAYER | AV_PIX_FMT_FLAG_BITSTREAM | AV_PIX_FMT_FLAG_XYZ;
    if (!descriptor || descriptor->nb_components != 3 || (descriptor->flags & rejected) || frame->hw_frames_ctx)
    { throw CoreError(ErrorCode::Unsupported, "Color resolution requires opaque three-component integer RGB or YUV."); }
    for (int index = 0; index < 3; ++index)
    {
        if (descriptor->comp[index].depth < 8 || descriptor->comp[index].depth > 16)
        { throw CoreError(ErrorCode::Unsupported, "Color component depth must be between eight and sixteen bits."); }
    }
    if ((frame->flags & (AV_FRAME_FLAG_CORRUPT | AV_FRAME_FLAG_INTERLACED)) || frame->decode_error_flags)
    { throw CoreError(ErrorCode::Unsupported, "Corrupt or interlaced frames require a separate processing policy."); }
    for (int index = 0; index < frame->nb_side_data; ++index)
    {
        const auto type = frame->side_data[index]->type;
        if (type == AV_FRAME_DATA_DISPLAYMATRIX || type == AV_FRAME_DATA_STEREO3D ||
            type == AV_FRAME_DATA_DYNAMIC_HDR_PLUS || type == AV_FRAME_DATA_DOVI_RPU_BUFFER ||
            type == AV_FRAME_DATA_DOVI_METADATA || type == AV_FRAME_DATA_DYNAMIC_HDR_VIVID ||
            type == AV_FRAME_DATA_DYNAMIC_HDR_SMPTE_2094_APP5 || type == AV_FRAME_DATA_ICC_PROFILE ||
            type == AV_FRAME_DATA_RAW_COLOR_PARAMS || type == AV_FRAME_DATA_FILM_GRAIN_PARAMS ||
            type == AV_FRAME_DATA_AMBIENT_VIEWING_ENVIRONMENT)
        { throw CoreError(ErrorCode::Unsupported, "Unsupported display, dynamic HDR or auxiliary color metadata."); }
    }
    ResolvedColor result{frame->color_range, frame->colorspace, frame->color_primaries, frame->color_trc,
        frame->chroma_location, frame->alpha_mode, 0};
    const auto rgb = (descriptor->flags & AV_PIX_FMT_FLAG_RGB) != 0;
    const auto subsampled = !rgb && (descriptor->log2_chroma_w || descriptor->log2_chroma_h);
    const auto missing = result.range == AVCOL_RANGE_UNSPECIFIED || result.matrix == AVCOL_SPC_UNSPECIFIED ||
        result.primaries == AVCOL_PRI_UNSPECIFIED || result.transfer == AVCOL_TRC_UNSPECIFIED ||
        (subsampled && result.chromaLocation == AVCHROMA_LOC_UNSPECIFIED);
    if (missing && (context.hdrEvidence || HasHdrEvidence(frame)))
    { throw CoreError(ErrorCode::Unsupported, "HDR evidence requires explicit supported range, matrix, primaries, transfer and chroma."); }
    const auto pal = !rgb && visibleHeight == 576 && visibleWidth < 1280;
    const auto ntsc = !rgb && (visibleHeight == 480 || visibleHeight == 486) && visibleWidth < 1280;
    const auto hd = visibleWidth >= 1280 || visibleHeight > 576;
    if (result.range == AVCOL_RANGE_UNSPECIFIED)
    { result.range = rgb || JpegFormat(format) || JpegFormat(context.sourcePixelFormat) ? AVCOL_RANGE_JPEG : AVCOL_RANGE_MPEG; result.inferredFields |= 1; }
    if (result.matrix == AVCOL_SPC_UNSPECIFIED)
    {
        result.matrix = rgb ? AVCOL_SPC_RGB : result.primaries == AVCOL_PRI_BT709 ? AVCOL_SPC_BT709 :
            result.primaries == AVCOL_PRI_BT470BG ? AVCOL_SPC_BT470BG : result.primaries == AVCOL_PRI_SMPTE170M ? AVCOL_SPC_SMPTE170M :
            hd ? AVCOL_SPC_BT709 : pal ? AVCOL_SPC_BT470BG : AVCOL_SPC_SMPTE170M;
        result.inferredFields |= 2;
    }
    if (result.primaries == AVCOL_PRI_UNSPECIFIED)
    {
        const auto matrix601 = result.matrix == AVCOL_SPC_BT470BG || result.matrix == AVCOL_SPC_SMPTE170M;
        result.primaries = matrix601 && pal ? AVCOL_PRI_BT470BG : matrix601 && ntsc ? AVCOL_PRI_SMPTE170M : AVCOL_PRI_BT709;
        result.inferredFields |= 4;
    }
    if (result.transfer == AVCOL_TRC_UNSPECIFIED)
    {
        result.transfer = rgb || result.range == AVCOL_RANGE_JPEG ? AVCOL_TRC_IEC61966_2_1 :
            result.primaries == AVCOL_PRI_BT470BG || result.primaries == AVCOL_PRI_SMPTE170M ? AVCOL_TRC_SMPTE170M : AVCOL_TRC_BT709;
        result.inferredFields |= 8;
    }
    if (subsampled && result.chromaLocation == AVCHROMA_LOC_UNSPECIFIED)
    { result.chromaLocation = result.range == AVCOL_RANGE_JPEG ? AVCHROMA_LOC_CENTER : AVCHROMA_LOC_LEFT; result.inferredFields |= 16; }
    if ((result.range != AVCOL_RANGE_MPEG && result.range != AVCOL_RANGE_JPEG) ||
        (result.primaries != AVCOL_PRI_BT709 && result.primaries != AVCOL_PRI_BT470BG && result.primaries != AVCOL_PRI_SMPTE170M && result.primaries != AVCOL_PRI_BT2020) ||
        (result.transfer != AVCOL_TRC_BT709 && result.transfer != AVCOL_TRC_SMPTE170M && result.transfer != AVCOL_TRC_IEC61966_2_1 && result.transfer != AVCOL_TRC_SMPTE2084 && result.transfer != AVCOL_TRC_ARIB_STD_B67) ||
        result.alphaMode < AVALPHA_MODE_UNSPECIFIED || result.alphaMode >= AVALPHA_MODE_NB ||
        result.chromaLocation < AVCHROMA_LOC_UNSPECIFIED || result.chromaLocation >= AVCHROMA_LOC_NB)
    { throw CoreError(ErrorCode::Unsupported, "Explicit color metadata is unsupported."); }
    if ((rgb && (result.matrix != AVCOL_SPC_RGB || result.range != AVCOL_RANGE_JPEG)) ||
        (!rgb && result.matrix != AVCOL_SPC_BT709 && result.matrix != AVCOL_SPC_BT470BG && result.matrix != AVCOL_SPC_SMPTE170M && result.matrix != AVCOL_SPC_BT2020_NCL) ||
        ((JpegFormat(format) || JpegFormat(context.sourcePixelFormat)) && result.range != AVCOL_RANGE_JPEG))
    { throw CoreError(ErrorCode::Unsupported, "Pixel format and explicit matrix/range metadata conflict."); }
    return result;
}
void ApplyColor(AVFrame *frame, const ResolvedColor &color)
{
    frame->color_range = static_cast<AVColorRange>(color.range);
    frame->colorspace = static_cast<AVColorSpace>(color.matrix);
    frame->color_primaries = static_cast<AVColorPrimaries>(color.primaries);
    frame->color_trc = static_cast<AVColorTransferCharacteristic>(color.transfer);
    frame->chroma_location = static_cast<AVChromaLocation>(color.chromaLocation);
    frame->alpha_mode = static_cast<AVAlphaMode>(color.alphaMode);
}
}
