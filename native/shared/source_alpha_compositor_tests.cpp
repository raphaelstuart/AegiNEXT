#include "source_alpha_compositor.h"
#include "color_resolution.h"
#include <array>
#include <cmath>
#include <cstring>
#include <iostream>
#include <vector>
extern "C"
{
#include <libavutil/csp.h>
#include <libavutil/pixdesc.h>
}
using namespace aeginext::media;
namespace
{
void Require(bool value, const char *message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}
FramePointer Make(AVPixelFormat format, AVAlphaMode mode, AVColorTransferCharacteristic transfer)
{
    FramePointer frame(av_frame_alloc());
    Require(frame != nullptr, "Cannot allocate alpha fixture.");
    frame->format = format;
    frame->width = 3;
    frame->height = 2;
    const auto rgb = (av_pix_fmt_desc_get(format)->flags & AV_PIX_FMT_FLAG_RGB) != 0;
    frame->color_range = rgb ? AVCOL_RANGE_JPEG : AVCOL_RANGE_MPEG;
    frame->colorspace = rgb ? AVCOL_SPC_RGB : AVCOL_SPC_BT709;
    frame->color_primaries = AVCOL_PRI_BT709;
    frame->color_trc = transfer;
    frame->alpha_mode = mode;
    Require(av_frame_get_buffer(frame.get(), 32) == 0, "Cannot allocate alpha fixture pixels.");
    return frame;
}
std::vector<uint8_t> Snapshot(const AVFrame *frame)
{
    std::vector<uint8_t> result;
    for (const auto *buffer : frame->buf)
    {
        if (buffer)
        {
            result.insert(result.end(), buffer->data, buffer->data + buffer->size);
        }
    }
    return result;
}
void IntegerAlphaUsesLinearLightWithoutChangingSource()
{
    const std::array<double, 3> original{1, 0.25, 0.125};
    for (const auto mode : {AVALPHA_MODE_UNSPECIFIED, AVALPHA_MODE_STRAIGHT, AVALPHA_MODE_PREMULTIPLIED})
    {
        auto source = Make(AV_PIX_FMT_RGBA64LE, mode, AVCOL_TRC_IEC61966_2_1);
        const std::array<uint16_t, 3> alpha{0, 32768, 65535};
        for (int y = 0; y < source->height; ++y)
        {
            auto *row = reinterpret_cast<uint16_t *>(source->data[0] + y * source->linesize[0]);
            for (int x = 0; x < source->width; ++x)
            {
                for (int component = 0; component < 3; ++component)
                {
                    const auto factor = mode == AVALPHA_MODE_PREMULTIPLIED ? alpha[x] / 65535.0 : 1;
                    row[x * 4 + component] = static_cast<uint16_t>(std::lround(original[component] * factor * 65535));
                }
                row[x * 4 + 3] = alpha[x];
            }
        }
        const auto snapshot = Snapshot(source.get());
        SourceAlphaCompositor compositor;
        const auto *actual = compositor.Composite(source.get());
        Require(actual->format == AV_PIX_FMT_GBRP16LE && actual->alpha_mode == AVALPHA_MODE_UNSPECIFIED,
            "Matte output is not opaque source-domain sixteen-bit RGB.");
        const auto decode = av_csp_itu_eotf(source->color_trc);
        const auto encode = av_csp_itu_eotf_inv(source->color_trc);
        for (int x = 0; x < source->width; ++x)
        {
            auto expected = original;
            decode(203, 0, expected.data());
            for (auto &component : expected)
            {
                component *= alpha[x] / 65535.0;
            }
            encode(203, 0, expected.data());
            const std::array<int, 3> planes{2, 0, 1};
            for (int component = 0; component < 3; ++component)
            {
                const auto value = reinterpret_cast<const uint16_t *>(actual->data[planes[component]])[x];
                Require(std::abs(value - expected[component] * 65535) <= 5,
                    "Alpha matte differs from independent linear-light colored reference.");
            }
        }
        Require(Snapshot(source.get()) == snapshot && source->alpha_mode == mode,
            "Matte consumption modified source pixels or raw alpha association.");
    }
}
void PlanarAlphaUsesItsOwnDepthAndPreservesCropAndHdr()
{
    for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
    {
        auto source = Make(AV_PIX_FMT_YUVA444P12LE, AVALPHA_MODE_STRAIGHT, transfer);
        if (transfer != AVCOL_TRC_BT709)
        {
            source->color_primaries = AVCOL_PRI_BT2020;
            source->colorspace = AVCOL_SPC_BT2020_NCL;
        }
        source->crop_left = 1;
        const auto *descriptor = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(source->format));
        const auto maximum = (1u << descriptor->comp[3].depth) - 1;
        for (int y = 0; y < source->height; ++y)
        {
            for (int x = 0; x < source->width; ++x)
            {
                reinterpret_cast<uint16_t *>(source->data[0] + y * source->linesize[0])[x] = 235 * 16;
                reinterpret_cast<uint16_t *>(source->data[1] + y * source->linesize[1])[x] = 128 * 16;
                reinterpret_cast<uint16_t *>(source->data[2] + y * source->linesize[2])[x] = 128 * 16;
                reinterpret_cast<uint16_t *>(source->data[3] + y * source->linesize[3])[x] = static_cast<uint16_t>(x * maximum / 2);
            }
        }
        const auto snapshot = Snapshot(source.get());
        SourceAlphaCompositor compositor;
        const auto *actual = compositor.Composite(source.get());
        Require(actual->format == AV_PIX_FMT_YUV444P16LE && actual->crop_left == 1 && actual->color_trc == transfer,
            "Matte changed coded geometry or source HDR transfer.");
        const auto *luma = reinterpret_cast<const uint16_t *>(actual->data[0]);
        Require(std::abs(static_cast<int>(luma[0]) - 4096) <= 8, "Transparent YUV did not become legal-range black.");
        Require(luma[1] > luma[0] && luma[1] < luma[2], "Partial YUV alpha was ignored or applied twice.");
        Require(std::abs(static_cast<int>(luma[2]) - 60160) <= 16, "Opaque YUV alpha did not preserve white.");
        Require(Snapshot(source.get()) == snapshot, "Planar source alpha or color bytes changed.");
    }
}
void CancellationAndMetadataReuseDoNotLeakPriorFrames()
{
    auto source = Make(AV_PIX_FMT_RGBA64LE, AVALPHA_MODE_STRAIGHT, AVCOL_TRC_IEC61966_2_1);
    for (int y = 0; y < source->height; ++y)
    {
        auto *row = reinterpret_cast<uint16_t *>(source->data[0] + y * source->linesize[0]);
        for (int x = 0; x < source->width; ++x)
        {
            row[x * 4] = 65535;
            row[x * 4 + 1] = 16384;
            row[x * 4 + 2] = 8192;
            row[x * 4 + 3] = 32768;
        }
    }
    source->pts = 41;
    av_dict_set(&source->metadata, "source", "prior-frame", 0);
    Require(av_frame_new_side_data(source.get(), AV_FRAME_DATA_MASTERING_DISPLAY_METADATA, 1) != nullptr,
        "Cannot allocate cached metadata fixture.");
    const auto original = Snapshot(source.get());
    SourceAlphaCompositor compositor;
    auto checks = 0;
    auto cancelled = false;
    try
    {
        compositor.Composite(source.get(), [&]()
        {
            if (++checks == 2)
            {
                throw CoreError(ErrorCode::Cancelled, "controlled alpha cancellation");
            }
        });
    }
    catch (const CoreError &error)
    {
        cancelled = error.Code() == ErrorCode::Cancelled;
    }
    Require(cancelled && checks == 2 && Snapshot(source.get()) == original,
        "Source alpha cancellation was delayed or changed the raw source.");
    const auto *first = compositor.Composite(source.get());
    Require(first->pts == 41 && first->nb_side_data == 1 && av_dict_get(first->metadata, "source", nullptr, 0),
        "Fresh alpha conversion did not copy the current frame metadata.");
    const auto sample = reinterpret_cast<const uint16_t *>(first->data[2])[0];
    FramePointer retained(av_frame_clone(first));
    Require(retained != nullptr, "Cannot retain prior alpha output.");
    source->pts = 82;
    av_frame_remove_side_data(source.get(), AV_FRAME_DATA_MASTERING_DISPLAY_METADATA);
    av_dict_free(&source->metadata);
    for (int y = 0; y < source->height; ++y)
    {
        auto *row = reinterpret_cast<uint16_t *>(source->data[0] + y * source->linesize[0]);
        for (int x = 0; x < source->width; ++x)
        {
            row[x * 4] = 0;
        }
    }
    const auto *second = compositor.Composite(source.get());
    Require(second->pts == 82 && second->nb_side_data == 0 && av_dict_count(second->metadata) == 0 &&
        reinterpret_cast<const uint16_t *>(second->data[2])[0] == 0 &&
        reinterpret_cast<const uint16_t *>(retained->data[2])[0] == sample,
        "Cached source alpha output leaked prior metadata or partial cancelled pixels.");
    source->alpha_mode = static_cast<AVAlphaMode>(AVALPHA_MODE_NB);
    for (int y = 0; y < source->height; ++y)
    {
        auto *row = reinterpret_cast<uint16_t *>(source->data[0] + y * source->linesize[0]);
        for (int x = 0; x < source->width; ++x)
        {
            row[x * 4 + 3] = 65535;
        }
    }
    auto unsupported = false;
    try
    {
        compositor.Composite(source.get());
    }
    catch (const CoreError &error)
    {
        unsupported = error.Code() == ErrorCode::Unsupported;
    }
    Require(unsupported, "An invalid alpha association was accepted for an opaque alpha plane.");
}
}
int main()
{
    try
    {
        IntegerAlphaUsesLinearLightWithoutChangingSource();
        PlanarAlphaUsesItsOwnDepthAndPreservesCropAndHdr();
        CancellationAndMetadataReuseDoNotLeakPriorFrames();
        std::cout << "PASS source alpha: linear black matte, colored straight/premult/undefined, plane depths, crop/HDR and immutability\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
