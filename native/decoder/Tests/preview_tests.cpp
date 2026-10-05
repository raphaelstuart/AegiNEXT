#include "preview_converter.h"
#include "color_resolution.h"
#include <array>
#include <cmath>
#include <cstring>
#include <functional>
#include <iostream>
#include <vector>

extern "C"
{
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/cpu.h>
}

using namespace aeginext::decode;

namespace
{
void Require(bool value, const char *message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

template<typename Action>
void ExpectError(int32_t result, Action action)
{
    try
    {
        action();
    }
    catch (const aeginext::media::CoreError &error)
    {
        Require(static_cast<int32_t>(error.Code()) == result, error.what());
        return;
    }
    catch (const Error &error)
    {
        Require(error.Result() == result, error.what());
        return;
    }
    throw std::runtime_error("Expected preview error was not thrown.");
}

FramePointer MakeFrame(AVPixelFormat format, int width = 5, int height = 3)
{
    FramePointer frame(av_frame_alloc());
    Require(frame != nullptr, "Cannot allocate preview fixture.");
    frame->format = format;
    frame->width = width;
    frame->height = height;
    frame->color_range = AVCOL_RANGE_JPEG;
    frame->colorspace = AVCOL_SPC_RGB;
    frame->color_primaries = AVCOL_PRI_BT709;
    frame->color_trc = AVCOL_TRC_IEC61966_2_1;
    frame->chroma_location = AVCHROMA_LOC_UNSPECIFIED;
    CheckAv(av_frame_get_buffer(frame.get(), 32), AN_DECODE_NATIVE_FAILURE, "av_frame_get_buffer(preview test)");
    for (int row = 0; row < height; ++row)
    {
        std::memset(frame->data[0] + row * frame->linesize[0], 0, frame->linesize[0]);
    }
    return frame;
}

an_preview_request Request(const FrameOwner &owner, uint32_t width = 0, uint32_t height = 0)
{
    const auto &info = owner.Info();
    aeginext::media::ResolvedColor color{info.color_range, info.color_matrix, info.color_primaries,
        info.color_transfer, info.chroma_location, info.alpha_mode, 0};
    try { color = aeginext::media::ResolveColor(owner.NativeFrame(), owner.ColorContext()); }
    catch (const aeginext::media::CoreError &) { }
    return {
        sizeof(an_preview_request), AN_DECODE_ABI_VERSION,
        width ? width : info.width - info.crop_left - info.crop_right,
        height ? height : info.height - info.crop_top - info.crop_bottom,
        color.range, color.matrix, color.primaries, color.transfer,
        color.chromaLocation, color.alphaMode, 0, 0
    };
}

std::vector<uint8_t> Convert(PreviewConverter &converter, const FrameOwner &owner,
    uint32_t width = 0, uint32_t height = 0)
{
    const auto request = Request(owner, width, height);
    std::vector<uint8_t> output(static_cast<size_t>(request.width) * request.height * 4);
    converter.Convert(owner, request, output.data(), output.size());
    return output;
}

FramePointer MakeRamp(AVColorTransferCharacteristic transfer, const std::vector<double> &levels)
{
    auto frame = MakeFrame(AV_PIX_FMT_YUV444P16LE, static_cast<int>(levels.size()), 2);
    frame->colorspace = AVCOL_SPC_BT2020_NCL;
    frame->color_primaries = AVCOL_PRI_BT2020;
    frame->color_trc = transfer;
    for (int row = 0; row < frame->height; ++row)
    {
        for (size_t x = 0; x < levels.size(); ++x)
        {
            const auto code = static_cast<uint16_t>(std::lround(levels[x] * 65535));
            const uint16_t center = 32768;
            std::memcpy(frame->data[0] + row * frame->linesize[0] + x * 2, &code, 2);
            std::memcpy(frame->data[1] + row * frame->linesize[1] + x * 2, &center, 2);
            std::memcpy(frame->data[2] + row * frame->linesize[2] + x * 2, &center, 2);
        }
    }
    return frame;
}

double EncodePq(double nits)
{
    const double p = std::pow(nits / 10000.0, 2610.0 / 16384.0);
    return std::pow((3424.0 / 4096.0 + 2413.0 / 128.0 * p) /
        (1.0 + 2392.0 / 128.0 * p), 2523.0 / 32.0);
}

void PqAndHlgUsePerceptualMapping()
{
    PreviewConverter converter;
    std::vector<double> levels;
    for (const auto nits : {0.0, 203.0, 400.0, 1000.0, 4000.0, 10000.0})
    {
        levels.push_back(EncodePq(nits));
    }
    FrameOwner pq(MakeRamp(AVCOL_TRC_SMPTE2084, levels), {1, 25});
    const auto output = Convert(converter, pq);
    Require(output[0] <= 1 && output[20] >= 254, "PQ black or peak did not map to SDR endpoints.");
    for (size_t pixel = 1; pixel < levels.size(); ++pixel)
    {
        Require(output[pixel * 4] > output[(pixel - 1) * 4] + 5,
            "PQ highlights collapsed; perceptual mapping may have reverted to clipping.");
        Require(std::abs(output[pixel * 4] - output[pixel * 4 + 1]) <= 1 &&
            std::abs(output[pixel * 4] - output[pixel * 4 + 2]) <= 1 && output[pixel * 4 + 3] == 255,
            "PQ neutral ramp changed hue or opacity.");
    }
    Require(output[4] < 200 && output[12] < 230, "PQ mapping resembles the default clipping intent.");
    FrameOwner hlg(MakeRamp(AVCOL_TRC_ARIB_STD_B67, {0, 0.25, 0.5, 0.75, 1}), {1, 25});
    const auto hlgOutput = Convert(converter, hlg);
    const std::array<int, 5> expected{0, 51, 108, 169, 255};
    for (size_t pixel = 0; pixel < expected.size(); ++pixel)
    {
        Require(std::abs(static_cast<int>(hlgOutput[pixel * 4]) - expected[pixel]) <= 3,
            "HLG reference-display mapping differs from the fixed CPU CMS oracle.");
    }
}

void CropNegativeStrideAndResizeAreExact()
{
    auto frame = MakeFrame(AV_PIX_FMT_RGB24, 5, 3);
    for (int row = 0; row < 3; ++row)
    {
        for (int x = 0; x < 5; ++x)
        {
            const std::array<uint8_t, 3> rgb{static_cast<uint8_t>(row * 60 + x * 5),
                static_cast<uint8_t>(x * 40), static_cast<uint8_t>(row * 80)};
            std::memcpy(frame->data[0] + row * frame->linesize[0] + x * 3, rgb.data(), 3);
        }
    }
    frame->data[0] += 2 * frame->linesize[0];
    frame->linesize[0] = -frame->linesize[0];
    frame->crop_left = 1;
    frame->crop_right = 1;
    frame->crop_top = 1;
    FrameOwner owner(std::move(frame), {1, 25});
    std::vector<uint8_t> before(owner.Plane(0).tight_byte_count);
    owner.CopyPlane(0, before.data(), before.size());
    const auto infoBefore = owner.Info();
    PreviewConverter converter;
    const auto output = Convert(converter, owner);
    Require(output.size() == 3 * 2 * 4, "Visible odd crop dimensions changed.");
    for (int row = 0; row < 2; ++row)
    {
        for (int x = 0; x < 3; ++x)
        {
            const auto source = ((row + 1) * 5 + (x + 1)) * 3;
            const auto target = (row * 3 + x) * 4;
            Require(std::abs(output[target] - before[source + 2]) <= 1 &&
                std::abs(output[target + 1] - before[source + 1]) <= 1 &&
                std::abs(output[target + 2] - before[source]) <= 1 && output[target + 3] == 255,
                "Exact crop or negative stride changed visible RGB samples.");
        }
    }
    Require(Convert(converter, owner, 6, 4).size() == 6 * 4 * 4, "Preview resize ignored requested dimensions.");
    std::vector<uint8_t> after(before.size());
    owner.CopyPlane(0, after.data(), after.size());
    Require(before == after && std::memcmp(&infoBefore, &owner.Info(), sizeof(infoBefore)) == 0,
        "Preview mutated original pixels or frame metadata.");
}

void StaticHdrPolicyKeepsSourceUnchanged()
{
    PreviewConverter converter;
    auto hlg = MakeRamp(AVCOL_TRC_ARIB_STD_B67, {0, 0.25, 0.5, 0.75, 1});
    FrameOwner baseline(FramePointer(av_frame_clone(hlg.get())), {1, 25});
    auto *mastering = av_mastering_display_metadata_create_side_data(hlg.get());
    Require(mastering != nullptr, "Cannot create mastering fixture.");
    mastering->has_luminance = 1;
    mastering->min_luminance = {1, 1};
    mastering->max_luminance = {4000, 1};
    auto *cll = av_content_light_metadata_create_side_data(hlg.get());
    Require(cll != nullptr, "Cannot create CLL fixture.");
    cll->MaxCLL = 10000;
    cll->MaxFALL = 2000;
    FrameOwner owner(std::move(hlg), {1, 25});
    const auto before = owner.Hdr();
    Require(Convert(converter, owner) == Convert(converter, baseline),
        "HLG inherited a variable mastering display or CLL exposure.");
    Require(std::memcmp(&before, &owner.Hdr(), sizeof(before)) == 0 && owner.Info().side_data_count == 2,
        "Preview stripped metadata from its source.");
    auto pq = MakeRamp(AVCOL_TRC_SMPTE2084, {EncodePq(100), EncodePq(400), EncodePq(1000)});
    FrameOwner pqDefault(FramePointer(av_frame_clone(pq.get())), {1, 25});
    mastering = av_mastering_display_metadata_create_side_data(pq.get());
    mastering->has_luminance = 1;
    mastering->min_luminance = {0, 1};
    mastering->max_luminance = {1000, 1};
    FrameOwner pqMastered(std::move(pq), {1, 25});
    Require(Convert(converter, pqMastered) != Convert(converter, pqDefault), "PQ mastering peak was ignored by the CMS.");
}

void LimitedRangeAndOddSubsampledCrop()
{
    auto source = MakeFrame(AV_PIX_FMT_YUV420P, 5, 3);
    source->color_range = AVCOL_RANGE_MPEG;
    source->colorspace = AVCOL_SPC_BT709;
    source->color_trc = AVCOL_TRC_BT709;
    source->chroma_location = AVCHROMA_LOC_LEFT;
    for (int row = 0; row < 3; ++row)
    {
        for (int x = 0; x < 5; ++x)
        {
            source->data[0][row * source->linesize[0] + x] = x % 2 ? 235 : 16;
        }
    }
    for (int plane = 1; plane < 3; ++plane)
    {
        for (int row = 0; row < 2; ++row)
        {
            std::memset(source->data[plane] + row * source->linesize[plane], 128, 3);
        }
    }
    FramePointer cropped(av_frame_clone(source.get()));
    cropped->crop_left = 1;
    cropped->crop_top = 1;
    FrameOwner full(std::move(source), {1, 25});
    FrameOwner visible(std::move(cropped), {1, 25});
    PreviewConverter converter;
    const auto original = Convert(converter, full);
    Require(original[0] <= 1 && original[4] >= 254, "Limited-range SDR black/white conversion is incorrect.");
    const auto result = Convert(converter, visible);
    for (int row = 0; row < 2; ++row)
    {
        Require(std::memcmp(result.data() + row * 4 * 4, original.data() + ((row + 1) * 5 + 1) * 4, 4 * 4) == 0,
            "Odd subsampled crop was rounded to a chroma or SIMD boundary.");
    }
    auto conflict = MakeFrame(AV_PIX_FMT_YUVJ420P);
    conflict->color_range = AVCOL_RANGE_MPEG;
    conflict->colorspace = AVCOL_SPC_BT709;
    conflict->chroma_location = AVCHROMA_LOC_LEFT;
    FrameOwner conflictOwner(std::move(conflict), {1, 25});
    ExpectError(AN_DECODE_UNSUPPORTED, [&]() { Convert(converter, conflictOwner); });
}

FramePointer MakeSubsampledColor(int width, int height, uint8_t luma, uint8_t u, uint8_t v)
{
    auto frame = MakeFrame(AV_PIX_FMT_YUV420P, width, height);
    frame->color_range = AVCOL_RANGE_MPEG;
    frame->colorspace = AVCOL_SPC_BT709;
    frame->color_trc = AVCOL_TRC_BT709;
    frame->chroma_location = AVCHROMA_LOC_LEFT;
    for (int row = 0; row < height; ++row)
    {
        std::memset(frame->data[0] + row * frame->linesize[0], luma, width);
    }
    for (int row = 0; row < (height + 1) / 2; ++row)
    {
        std::memset(frame->data[1] + row * frame->linesize[1], u, (width + 1) / 2);
        std::memset(frame->data[2] + row * frame->linesize[2], v, (width + 1) / 2);
    }
    return frame;
}

void SubsampledColorsDoNotAcquireAlternatingRowHues()
{
    PreviewConverter converter;
    for (const auto &chroma : std::array<std::array<uint8_t, 2>, 4>{{{100, 130}, {150, 90}, {100, 90}, {150, 160}}})
    {
        FrameOwner owner(MakeSubsampledColor(16, 16, 160, chroma[0], chroma[1]), {1, 30});
        const auto output = Convert(converter, owner);
        for (int row = 0; row < 16; ++row)
        {
            for (int x = 0; x < 16; ++x)
            {
                for (int component = 0; component < 4; ++component)
                {
                    Require(std::abs(output[(row * 16 + x) * 4 + component] - output[component]) <= 1,
                        "Uniform non-neutral YUV420 acquired alternating row hues during CMS conversion.");
                }
            }
        }
        if (chroma[0] == 100 && chroma[1] == 130)
        {
            Require(output[1] > output[0] + 40 && output[2] > output[0] + 40,
                "Yellow-green YUV420 patch changed hue or lost chroma.");
        }
    }
}

FramePointer MakeThreadedColorFixture(AVColorTransferCharacteristic transfer)
{
    auto frame = MakeFrame(AV_PIX_FMT_YUV420P10LE, 129, 139);
    const auto hdr = transfer == AVCOL_TRC_SMPTE2084 || transfer == AVCOL_TRC_ARIB_STD_B67;
    frame->color_range = AVCOL_RANGE_MPEG;
    frame->colorspace = hdr ? AVCOL_SPC_BT2020_NCL : AVCOL_SPC_BT709;
    frame->color_primaries = hdr ? AVCOL_PRI_BT2020 : AVCOL_PRI_BT709;
    frame->color_trc = transfer;
    frame->chroma_location = AVCHROMA_LOC_LEFT;
    frame->crop_left = 1;
    frame->crop_top = 1;
    frame->crop_right = 2;
    frame->crop_bottom = 2;
    for (int row = 0; row < frame->height; ++row)
    {
        for (int x = 0; x < frame->width; ++x)
        {
            const auto value = static_cast<uint16_t>(64 + (x * 17 + row * 11) % 877);
            std::memcpy(frame->data[0] + row * frame->linesize[0] + x * 2, &value, 2);
        }
    }
    for (int row = 0; row < (frame->height + 1) / 2; ++row)
    {
        for (int x = 0; x < (frame->width + 1) / 2; ++x)
        {
            const auto u = static_cast<uint16_t>(392 + (x * 13 + row * 17) % 241);
            const auto v = static_cast<uint16_t>(392 + (x * 19 + row * 7) % 241);
            std::memcpy(frame->data[1] + row * frame->linesize[1] + x * 2, &u, 2);
            std::memcpy(frame->data[2] + row * frame->linesize[2] + x * 2, &v, 2);
        }
    }
    return frame;
}

void CmsThreadBudgetsAreBoundedAndPreservePixels()
{
    PreviewConverter automatic;
    Require(automatic.ThreadBudget() >= 1 && automatic.ThreadBudget() <= 4 &&
        (automatic.ThreadBudget() <= av_cpu_count() || automatic.ThreadBudget() == 1),
        "Default CMS thread budget is outside its CPU and four-thread bounds.");
    ExpectError(AN_DECODE_INVALID_ARGUMENT, []() { PreviewConverter invalid(0); });
    ExpectError(AN_DECODE_INVALID_ARGUMENT, []() { PreviewConverter invalid(-1); });
    ExpectError(AN_DECODE_INVALID_ARGUMENT, []() { PreviewConverter invalid(5); });
    PreviewConverter single(1);
    for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_IEC61966_2_1, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
    {
        FrameOwner owner(MakeThreadedColorFixture(transfer), {1, 30});
        const auto original = Convert(single, owner);
        const auto resized = Convert(single, owner, 67, 73);
        for (int budget = 2; budget <= 4; ++budget)
        {
            PreviewConverter parallel(budget);
            Require(parallel.ThreadBudget() == budget, "Explicit CMS thread budget was not retained.");
            Require(Convert(parallel, owner) == original && Convert(parallel, owner, 67, 73) == resized,
                "CMS thread count changed SDR/PQ/HLG color pixels or odd cropped/resized rows.");
        }
        Require(Convert(automatic, owner) == original, "Default CMS thread budget changed output pixels.");
    }
    FrameOwner constant(MakeSubsampledColor(129, 139, 160, 100, 130), {1, 30});
    PreviewConverter parallel(4);
    Require(Convert(single, constant) == Convert(parallel, constant),
        "Thread slicing changed a non-neutral subsampled constant color.");
}

void InvalidRequestsAndUnsupportedSourcesFail()
{
    PreviewConverter converter;
    FrameOwner owner(MakeFrame(AV_PIX_FMT_RGB24), {1, 25});
    auto request = Request(owner);
    std::array<uint8_t, 62> output{};
    output.fill(0xee);
    ExpectError(AN_DECODE_INVALID_ARGUMENT, [&]() { converter.Convert(owner, request, output.data(), 59); });
    Require(output[0] == 0xee, "Insufficient output capacity was written before validation.");
    ExpectError(AN_DECODE_INVALID_ARGUMENT, [&]() { converter.Convert(owner, request, nullptr, output.size()); });
    request.width = 0;
    ExpectError(AN_DECODE_INVALID_ARGUMENT, [&]() { converter.Convert(owner, request, output.data(), output.size()); });
    request = Request(owner, 16777217, 1);
    ExpectError(AN_DECODE_INVALID_ARGUMENT, [&]() { converter.Convert(owner, request, output.data(), UINT64_MAX); });
    request = Request(owner);
    request.color_transfer = AVCOL_TRC_BT709;
    ExpectError(AN_DECODE_INVALID_ARGUMENT, [&]() { converter.Convert(owner, request, output.data(), output.size()); });
    request = Request(owner);
    request.flags = 1;
    ExpectError(AN_DECODE_INVALID_ARGUMENT, [&]() { converter.Convert(owner, request, output.data(), output.size()); });
    for (const auto format : {AV_PIX_FMT_RGBA, AV_PIX_FMT_GBRPF32LE, AV_PIX_FMT_GRAY8, AV_PIX_FMT_PAL8})
    {
        FrameOwner unsupported(MakeFrame(format), {1, 25});
        const auto args = Request(unsupported);
        ExpectError(AN_DECODE_UNSUPPORTED, [&]() { converter.Convert(unsupported, args, output.data(), output.size()); });
    }
    for (const auto type : {AV_FRAME_DATA_DISPLAYMATRIX, AV_FRAME_DATA_STEREO3D, AV_FRAME_DATA_DYNAMIC_HDR_PLUS,
        AV_FRAME_DATA_DOVI_RPU_BUFFER, AV_FRAME_DATA_DOVI_METADATA, AV_FRAME_DATA_DYNAMIC_HDR_VIVID,
        AV_FRAME_DATA_DYNAMIC_HDR_SMPTE_2094_APP5, AV_FRAME_DATA_ICC_PROFILE, AV_FRAME_DATA_RAW_COLOR_PARAMS})
    {
        auto frame = MakeFrame(AV_PIX_FMT_RGB24);
        Require(av_frame_new_side_data(frame.get(), type, 1) != nullptr, "Cannot create rejected side-data fixture.");
        FrameOwner unsupported(std::move(frame), {1, 25});
        const auto args = Request(unsupported);
        ExpectError(AN_DECODE_UNSUPPORTED, [&]() { converter.Convert(unsupported, args, output.data(), output.size()); });
    }
    auto unknown = MakeFrame(AV_PIX_FMT_RGB24);
    unknown->color_trc = AVCOL_TRC_UNSPECIFIED;
    FrameOwner unknownOwner(std::move(unknown), {1, 25});
    Require(!Convert(converter, unknownOwner).empty(), "Missing SDR transfer should be inferred.");
    auto interlaced = MakeFrame(AV_PIX_FMT_RGB24);
    interlaced->flags |= AV_FRAME_FLAG_INTERLACED;
    FrameOwner interlacedOwner(std::move(interlaced), {1, 25});
    ExpectError(AN_DECODE_UNSUPPORTED, [&]() { Convert(converter, interlacedOwner); });
    auto corrupt = MakeFrame(AV_PIX_FMT_RGB24);
    corrupt->flags |= AV_FRAME_FLAG_CORRUPT;
    FrameOwner corruptOwner(std::move(corrupt), {1, 25});
    ExpectError(AN_DECODE_UNSUPPORTED, [&]() { Convert(converter, corruptOwner); });
    auto partial = MakeFrame(AV_PIX_FMT_RGB24);
    partial->decode_error_flags = FF_DECODE_ERROR_MISSING_REFERENCE;
    FrameOwner partialOwner(std::move(partial), {1, 25});
    ExpectError(AN_DECODE_UNSUPPORTED, [&]() { Convert(converter, partialOwner); });
    auto invalidMastering = MakeRamp(AVCOL_TRC_SMPTE2084, {0, 0.5, 1});
    auto *mastering = av_mastering_display_metadata_create_side_data(invalidMastering.get());
    mastering->has_luminance = 1;
    mastering->min_luminance = {0, 1};
    mastering->max_luminance = {0, 1};
    FrameOwner invalidMasteringOwner(std::move(invalidMastering), {1, 25});
    ExpectError(AN_DECODE_UNSUPPORTED, [&]() { Convert(converter, invalidMasteringOwner); });
    converter.Convert(owner, Request(owner), output.data(), output.size());
    Require(output[60] == 0xee && output[61] == 0xee, "Preview wrote outside the tight output range.");
}

void PreviewAbiAndLifecycle()
{
    static_assert(sizeof(an_preview_backend_info) == 16);
    static_assert(sizeof(an_preview_request) == 48);
    static_assert(offsetof(an_preview_request, color_range) == 16);
    static_assert(offsetof(an_preview_request, flags) == 40);
    Require((an_decode_features() & AN_DECODE_FEATURE_SDR_PREVIEW) != 0, "Preview capability is missing.");
    std::array<char, 512> error{};
    an_preview_backend_info backend{sizeof(backend), AN_DECODE_ABI_VERSION, 0, 0};
    Require(an_preview_get_backend_info(&backend, error.data(), error.size()) == AN_DECODE_OK, error.data());
    Require(backend.compile_swscale == backend.runtime_swscale && backend.runtime_swscale == AV_VERSION_INT(10, 1, 102),
        "Preview swscale versions do not match the pinned SDK.");
    backend.struct_size = 0;
    Require(an_preview_get_backend_info(&backend, error.data(), error.size()) == AN_DECODE_INVALID_ARGUMENT,
        "Invalid preview backend ABI was accepted.");
    void *handle = nullptr;
    Require(an_preview_converter_create(&handle, error.data(), error.size()) == AN_DECODE_OK, error.data());
    Require(an_preview_live_converters() == 1, "Preview converter counter did not increase.");
    an_preview_request request{sizeof(request), AN_DECODE_ABI_VERSION, 1, 1};
    std::array<uint8_t, 4> output{};
    Require(an_preview_convert(handle, nullptr, &request, output.data(), output.size(), error.data(), error.size()) ==
        AN_DECODE_INVALID_ARGUMENT, "Null borrowed frame was accepted.");
    an_preview_converter_destroy(handle);
    an_preview_converter_destroy(handle);
    an_preview_converter_destroy(nullptr);
    Require(an_preview_live_converters() == 0 && an_decode_live_frames() == 0 && an_decode_live_decoders() == 0,
        "Preview leaked registered native handles.");
    Require(an_preview_convert(handle, nullptr, &request, output.data(), output.size(), error.data(), error.size()) ==
        AN_DECODE_INVALID_ARGUMENT, "Released preview converter was accepted.");
    Require(an_preview_converter_create(nullptr, error.data(), 1) == AN_DECODE_INVALID_ARGUMENT && error[0] == '\0',
        "Bounded preview error buffer failed.");
}
}

int main()
{
    const std::vector<std::pair<const char *, std::function<void()>>> tests{
        {"PQ and HLG perceptual mapping", PqAndHlgUsePerceptualMapping},
        {"exact crop negative stride and resize", CropNegativeStrideAndResizeAreExact},
        {"static HDR policy and immutable source", StaticHdrPolicyKeepsSourceUnchanged},
        {"limited range and odd subsampled crop", LimitedRangeAndOddSubsampledCrop},
        {"subsampled colors preserve uniform row hue", SubsampledColorsDoNotAcquireAlternatingRowHues},
        {"CMS thread budgets preserve pixels", CmsThreadBudgetsAreBoundedAndPreservePixels},
        {"invalid requests and unsupported sources", InvalidRequestsAndUnsupportedSourcesFail},
        {"preview ABI and lifecycle", PreviewAbiAndLifecycle}
    };
    int failures = 0;
    for (const auto &[name, test] : tests)
    {
        try
        {
            test();
            std::cout << "PASS " << name << '\n';
        }
        catch (const std::exception &error)
        {
            ++failures;
            std::cerr << "FAIL " << name << ": " << error.what() << '\n';
        }
    }
    return failures ? 1 : 0;
}
