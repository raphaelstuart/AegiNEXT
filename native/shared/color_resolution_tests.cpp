#include "color_resolution.h"
#include <array>
#include <iostream>
extern "C"
{
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/pixdesc.h>
}
using namespace aeginext::media;
namespace
{
void Require(bool value, const char *message)
{ if (!value) { throw std::runtime_error(message); } }
FramePointer Make(AVPixelFormat format = AV_PIX_FMT_YUV420P, int width = 1280, int height = 720)
{
    FramePointer frame(av_frame_alloc());
    if (!frame) { throw std::bad_alloc(); }
    frame->format = format; frame->width = width; frame->height = height;
    Require(av_frame_get_buffer(frame.get(), 32) == 0, "Cannot allocate fixture pixels.");
    return frame;
}
template<typename Action> void Unsupported(Action action)
{
    try { action(); }
    catch (const CoreError &error) { Require(error.Code() == ErrorCode::Unsupported, "Wrong rejection classification."); return; }
    throw std::runtime_error("Unsupported color was accepted.");
}
void DefaultsAndDependencies()
{
    struct Row { AVPixelFormat format; int width, height, range, matrix, primaries, transfer, chroma;
        int expectedRange, expectedMatrix, expectedPrimaries, expectedTransfer, expectedChroma; uint32_t inferred; };
    const std::array rows{
        Row{AV_PIX_FMT_YUV420P,1280,720,0,2,2,2,0,1,1,1,1,1,31},
        Row{AV_PIX_FMT_YUV420P,720,576,0,2,2,2,0,1,5,5,6,1,31},
        Row{AV_PIX_FMT_YUV420P,720,480,0,2,2,2,0,1,6,6,6,1,31},
        Row{AV_PIX_FMT_YUV420P,720,486,0,2,2,2,0,1,6,6,6,1,31},
        Row{AV_PIX_FMT_YUV420P,640,360,0,2,2,2,0,1,6,1,1,1,31},
        Row{AV_PIX_FMT_YUV420P,720,576,1,2,1,2,0,1,1,1,1,1,26},
        Row{AV_PIX_FMT_YUV420P,1920,1080,1,2,5,2,0,1,5,5,6,1,26},
        Row{AV_PIX_FMT_YUV420P,1920,1080,1,2,6,2,0,1,6,6,6,1,26},
        Row{AV_PIX_FMT_YUV420P,720,576,1,1,2,2,0,1,1,1,1,1,28},
        Row{AV_PIX_FMT_YUV420P,720,480,1,1,2,2,0,1,1,1,1,1,28},
        Row{AV_PIX_FMT_YUV420P,720,576,1,6,2,2,0,1,6,5,6,1,28},
        Row{AV_PIX_FMT_YUV420P,1280,720,2,2,2,2,0,2,1,1,13,2,30},
        Row{AV_PIX_FMT_YUVJ420P,1280,720,0,2,2,2,0,2,1,1,13,2,31},
        Row{AV_PIX_FMT_GBRP,1280,720,0,2,2,2,0,2,0,1,13,0,15},
        Row{AV_PIX_FMT_GBRP,1280,720,2,0,1,13,0,2,0,1,13,0,0},
        Row{AV_PIX_FMT_YUV444P,1280,720,1,1,1,1,0,1,1,1,1,0,0},
        Row{AV_PIX_FMT_NV12,1280,720,0,2,2,2,0,1,1,1,1,1,31},
        Row{AV_PIX_FMT_P010LE,1280,720,0,2,2,2,0,1,1,1,1,1,31}
    };
    for (const auto &row : rows)
    {
        auto frame = Make(row.format, row.width, row.height);
        frame->color_range = static_cast<AVColorRange>(row.range);
        frame->colorspace = static_cast<AVColorSpace>(row.matrix);
        frame->color_primaries = static_cast<AVColorPrimaries>(row.primaries);
        frame->color_trc = static_cast<AVColorTransferCharacteristic>(row.transfer);
        frame->chroma_location = static_cast<AVChromaLocation>(row.chroma);
        const auto color = ResolveColor(frame.get());
        Require(color.range == row.expectedRange && color.matrix == row.expectedMatrix && color.primaries == row.expectedPrimaries &&
            color.transfer == row.expectedTransfer && color.chromaLocation == row.expectedChroma && color.inferredFields == row.inferred,
            "SDR dependency table differs from approved policy.");
        Require(frame->color_range == row.range && frame->colorspace == row.matrix && frame->color_primaries == row.primaries &&
            frame->color_trc == row.transfer && frame->chroma_location == row.chroma, "Raw frame facts were changed.");
        FramePointer clone(av_frame_clone(frame.get()));
        Require(clone != nullptr, "Cannot clone color fixture.");
        ApplyColor(clone.get(), color);
        const auto explicitColor = ResolveColor(clone.get());
        Require(explicitColor.inferredFields == 0 && explicitColor.range == color.range && explicitColor.matrix == color.matrix &&
            explicitColor.primaries == color.primaries && explicitColor.transfer == color.transfer &&
            explicitColor.chromaLocation == color.chromaLocation, "Missing-to-explicit transition changed effective color.");
    }
}
void VisibleGeometryIsBackendIndependent()
{
    auto coded = Make(AV_PIX_FMT_YUV420P, 720, 576);
    coded->crop_bottom = 2;
    auto downloaded = Make(AV_PIX_FMT_NV12, 720, 574);
    const auto source = ResolveColor(coded.get());
    const auto hardware = ResolveColor(downloaded.get());
    Require(source.range == hardware.range && source.matrix == hardware.matrix && source.primaries == hardware.primaries &&
        source.transfer == hardware.transfer && source.chromaLocation == hardware.chromaLocation,
        "Backend coded padding changed effective SDR defaults.");
    Require(source.primaries == AVCOL_PRI_BT709, "Cropping was ignored when selecting visible SD defaults.");
    coded->crop_bottom = 576;
    Unsupported([&]() { ResolveColor(coded.get()); });
    coded->crop_bottom = SIZE_MAX;
    Unsupported([&]() { ResolveColor(coded.get()); });
}
void ExplicitUnsupportedAndConflicts()
{
    for (const auto field : {0,1,2,3,4,5})
    {
        auto frame = Make(); frame->color_range = AVCOL_RANGE_MPEG; frame->colorspace = AVCOL_SPC_BT709;
        frame->color_primaries = AVCOL_PRI_BT709; frame->color_trc = AVCOL_TRC_BT709; frame->chroma_location = AVCHROMA_LOC_LEFT;
        if (field == 0) { frame->color_range = static_cast<AVColorRange>(99); }
        if (field == 1) { frame->colorspace = static_cast<AVColorSpace>(99); }
        if (field == 2) { frame->color_primaries = static_cast<AVColorPrimaries>(99); }
        if (field == 3) { frame->color_trc = static_cast<AVColorTransferCharacteristic>(99); }
        if (field == 4) { frame->chroma_location = static_cast<AVChromaLocation>(99); }
        if (field == 5) { frame->alpha_mode = static_cast<AVAlphaMode>(99); }
        Unsupported([&]() { ResolveColor(frame.get()); });
    }
    auto jpeg = Make(AV_PIX_FMT_YUVJ420P); jpeg->color_range = AVCOL_RANGE_MPEG;
    Unsupported([&]() { ResolveColor(jpeg.get()); });
    auto rgb = Make(AV_PIX_FMT_RGB24); rgb->colorspace = AVCOL_SPC_BT709;
    Unsupported([&]() { ResolveColor(rgb.get()); });
    auto yuv = Make(); yuv->colorspace = AVCOL_SPC_RGB;
    Unsupported([&]() { ResolveColor(yuv.get()); });
    for (const auto format : {AV_PIX_FMT_YA8, AV_PIX_FMT_GBRPF32LE, AV_PIX_FMT_GRAY8})
    {
        auto frame = Make(format);
        Unsupported([&]() { ResolveColor(frame.get()); });
    }
    for (const auto flag : {AV_FRAME_FLAG_INTERLACED, AV_FRAME_FLAG_CORRUPT})
    {
        auto frame = Make(); frame->flags = flag;
        Unsupported([&]() { ResolveColor(frame.get()); });
    }
}
void AlphaAssociationIsResolvedWithoutChangingRawFacts()
{
    for (const auto format : {AV_PIX_FMT_YUVA444P12LE, AV_PIX_FMT_GBRAP16LE, AV_PIX_FMT_RGBA})
    {
        for (const auto mode : {AVALPHA_MODE_UNSPECIFIED, AVALPHA_MODE_STRAIGHT, AVALPHA_MODE_PREMULTIPLIED})
        {
            auto frame = Make(format);
            frame->alpha_mode = mode;
            const auto resolved = ResolveColor(frame.get());
            Require(resolved.alphaMode == mode && frame->alpha_mode == mode,
                "Color resolution rewrote the raw alpha association.");
        }
    }
}
void HdrEvidenceAndUnsupportedSideData()
{
    for (const auto evidence : {0,1,2,3,4,5})
    {
        auto frame = Make();
        if (evidence == 0) { frame->color_trc = AVCOL_TRC_SMPTE2084; }
        if (evidence == 1) { frame->color_trc = AVCOL_TRC_ARIB_STD_B67; }
        if (evidence == 2) { frame->colorspace = AVCOL_SPC_BT2020_NCL; }
        if (evidence == 3) { frame->color_primaries = AVCOL_PRI_BT2020; }
        if (evidence == 4) { Require(av_mastering_display_metadata_create_side_data(frame.get()) != nullptr, "Cannot attach mastering."); }
        if (evidence == 5) { Require(av_content_light_metadata_create_side_data(frame.get()) != nullptr, "Cannot attach CLL."); }
        Unsupported([&]() { ResolveColor(frame.get()); });
    }
    auto frame = Make();
    Unsupported([&]() { ResolveColor(frame.get(), {true, AV_PIX_FMT_YUV420P}); });
    for (const auto type : {AV_FRAME_DATA_DISPLAYMATRIX, AV_FRAME_DATA_STEREO3D, AV_FRAME_DATA_DYNAMIC_HDR_PLUS,
        AV_FRAME_DATA_DOVI_RPU_BUFFER, AV_FRAME_DATA_DOVI_METADATA, AV_FRAME_DATA_ICC_PROFILE,
        AV_FRAME_DATA_RAW_COLOR_PARAMS, AV_FRAME_DATA_FILM_GRAIN_PARAMS, AV_FRAME_DATA_AMBIENT_VIEWING_ENVIRONMENT})
    {
        auto metadata = Make();
        Require(av_frame_new_side_data(metadata.get(), type, 1) != nullptr, "Cannot attach side data.");
        Unsupported([&]() { ResolveColor(metadata.get()); });
    }
    for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_IEC61966_2_1, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
    {
        auto explicitFrame = Make(); explicitFrame->color_range = AVCOL_RANGE_MPEG;
        explicitFrame->colorspace = AVCOL_SPC_BT2020_NCL; explicitFrame->color_primaries = AVCOL_PRI_BT2020;
        explicitFrame->color_trc = transfer; explicitFrame->chroma_location = AVCHROMA_LOC_LEFT;
        Require(ResolveColor(explicitFrame.get(), {true, AV_PIX_FMT_NONE}).inferredFields == 0, "Explicit HDR facts were inferred.");
    }
}
}
int main()
{
    try
    {
        ValidateBackend(); DefaultsAndDependencies(); VisibleGeometryIsBackendIndependent(); ExplicitUnsupportedAndConflicts();
        AlphaAssociationIsResolvedWithoutChangingRawFacts(); HdrEvidenceAndUnsupportedSideData();
        std::cout << "PASS shared SDR dependencies, raw immutability, unsupported conflicts and strict HDR guards\n";
        return 0;
    }
    catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
}
