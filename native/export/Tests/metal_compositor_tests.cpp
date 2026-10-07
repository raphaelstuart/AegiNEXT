#include "metal_sdr_compositor.h"
#include "color_pipeline.h"
#include "frame_row_executor.h"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#include <iostream>
#include <limits>
#include <memory>
#include <stdexcept>
#include <vector>

extern "C"
{
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
#include <libswscale/swscale.h>
}

using namespace aeginext::encode;
using namespace aeginext::encode::experimental;

namespace
{
using Clock = std::chrono::steady_clock;

struct FrameDeleter final
{
    void operator()(AVFrame *frame) const
    {
        av_frame_free(&frame);
    }
};
using Frame = std::unique_ptr<AVFrame, FrameDeleter>;

struct ScalerDeleter final
{
    void operator()(SwsContext *context) const
    {
        sws_free_context(&context);
    }
};
using Scaler = std::unique_ptr<SwsContext, ScalerDeleter>;

struct ErrorStatistics final
{
    int maximum = 0;
    uint64_t different = 0;
    uint64_t samples = 0;
    double squared = 0;
};

void Require(bool condition, const char *message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

void Check(int result, const char *message)
{
    if (result < 0)
    {
        throw std::runtime_error(message);
    }
}

Frame Allocate(int width, int height, AVPixelFormat format, AVColorRange range = AVCOL_RANGE_MPEG,
    AVColorTransferCharacteristic transfer = AVCOL_TRC_BT709)
{
    Frame result(av_frame_alloc());
    if (!result)
    {
        throw std::bad_alloc();
    }
    result->width = width;
    result->height = height;
    result->format = format;
    result->colorspace = AVCOL_SPC_BT709;
    result->color_primaries = AVCOL_PRI_BT709;
    result->color_trc = transfer;
    result->color_range = range;
    result->chroma_location = format == AV_PIX_FMT_YUV420P ? AVCHROMA_LOC_LEFT : AVCHROMA_LOC_UNSPECIFIED;
    Check(av_frame_get_buffer(result.get(), 32), "Allocate prototype frame");
    return result;
}

uint32_t Next(uint32_t &state)
{
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    return state;
}

void Fill444(AVFrame *frame, uint32_t seed)
{
    for (auto plane = 0; plane < 3; ++plane)
    {
        for (auto y = 0; y < frame->height; ++y)
        {
            auto *row = reinterpret_cast<uint16_t *>(frame->data[plane] + static_cast<ptrdiff_t>(y) * frame->linesize[plane]);
            for (auto x = 0; x < frame->width; ++x)
            {
                row[x] = static_cast<uint16_t>(Next(seed));
            }
        }
    }
}

void Fill420(AVFrame *frame, uint32_t seed)
{
    for (auto plane = 0; plane < 3; ++plane)
    {
        const auto width = plane ? (frame->width + 1) / 2 : frame->width;
        const auto height = plane ? (frame->height + 1) / 2 : frame->height;
        for (auto y = 0; y < height; ++y)
        {
            auto *row = frame->data[plane] + static_cast<ptrdiff_t>(y) * frame->linesize[plane];
            for (auto x = 0; x < width; ++x)
            {
                row[x] = static_cast<uint8_t>(16 + Next(seed) % (plane ? 225 : 220));
            }
        }
    }
}

std::vector<float> Layer(int width, int height, int mode)
{
    std::vector<float> result(static_cast<size_t>(width) * height * 4);
    uint32_t seed = 0x89ABCDEF;
    for (auto y = 0; y < height; ++y)
    {
        for (auto x = 0; x < width; ++x)
        {
            auto *pixel = &result[(static_cast<size_t>(y) * width + x) * 4];
            const auto alpha = mode == 0 ? 0.0f : mode == 1 ? 1.0f : mode == 2 ? 0.5f :
                static_cast<float>(Next(seed) % 1001) / 1000;
            pixel[3] = alpha;
            for (auto channel = 0; channel < 3; ++channel)
            {
                pixel[channel] = (static_cast<float>(Next(seed) % 3001) / 1000 - 0.5f) * alpha;
            }
        }
    }
    return result;
}

void CompositeCpu(const AVFrame *source, std::span<const float> layer, double referenceWhite,
    AVFrame *output, FrameRowExecutor &rows)
{
    const ColorPipeline color(source->colorspace, source->color_primaries, source->color_trc);
    const auto limited = source->color_range == AVCOL_RANGE_MPEG;
    const double yOffset = limited ? 4096 : 0;
    const double yScale = limited ? 56064 : 65535;
    const double uvScale = limited ? 57344 : 65535;
    rows.Execute(source->height, [&](size_t begin, size_t end)
    {
        for (auto y = begin; y < end; ++y)
        {
            const uint16_t *input[3]{};
            uint16_t *destination[3]{};
            for (auto plane = 0; plane < 3; ++plane)
            {
                input[plane] = reinterpret_cast<const uint16_t *>(source->data[plane] + static_cast<ptrdiff_t>(y) * source->linesize[plane]);
                destination[plane] = reinterpret_cast<uint16_t *>(output->data[plane] + static_cast<ptrdiff_t>(y) * output->linesize[plane]);
            }
            for (auto x = 0; x < source->width; ++x)
            {
                const auto *pixel = &layer[(y * source->width + x) * 4];
                if (pixel[3] == 0)
                {
                    for (auto plane = 0; plane < 3; ++plane)
                    {
                        destination[plane][x] = input[plane][x];
                    }
                    continue;
                }
                const auto encoded = color.Composite({(input[0][x] - yOffset) / yScale,
                    (input[1][x] - 32768.0) / uvScale, (input[2][x] - 32768.0) / uvScale},
                    {pixel[0], pixel[1], pixel[2], pixel[3]}, referenceWhite);
                for (auto plane = 0; plane < 3; ++plane)
                {
                    const auto value = encoded[plane] * (plane == 0 ? yScale : uvScale) + (plane == 0 ? yOffset : 32768);
                    Require(std::isfinite(value), "CPU oracle produced a non-finite sample");
                    destination[plane][x] = static_cast<uint16_t>(std::clamp(std::llround(value), 0LL, 65535LL));
                }
            }
        }
    });
}

ErrorStatistics Compare(const AVFrame *left, const AVFrame *right)
{
    ErrorStatistics result{};
    const auto bytes = left->format == AV_PIX_FMT_YUV444P16LE ? 2 : 1;
    for (auto plane = 0; plane < 3; ++plane)
    {
        const auto width = bytes == 1 && plane ? (left->width + 1) / 2 : left->width;
        const auto height = bytes == 1 && plane ? (left->height + 1) / 2 : left->height;
        for (auto y = 0; y < height; ++y)
        {
            const auto *a = left->data[plane] + static_cast<ptrdiff_t>(y) * left->linesize[plane];
            const auto *b = right->data[plane] + static_cast<ptrdiff_t>(y) * right->linesize[plane];
            for (auto x = 0; x < width; ++x)
            {
                const auto difference = std::abs(bytes == 2 ?
                    reinterpret_cast<const uint16_t *>(a)[x] - reinterpret_cast<const uint16_t *>(b)[x] : a[x] - b[x]);
                result.maximum = std::max(result.maximum, difference);
                result.different += difference != 0;
                ++result.samples;
                result.squared += static_cast<double>(difference) * difference;
            }
        }
    }
    return result;
}

template<typename Action>
void Reject(Action action, const char *message)
{
    try
    {
        action();
    }
    catch (const std::invalid_argument &)
    {
        return;
    }
    throw std::runtime_error(message);
}

void ValidateAliasing()
{
    MetalSdrCompositor metal(37, 19);
    auto source = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    auto output = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    Require(source->buf[0] && output->buf[0], "Alias fixture requires owned frame buffers");
    std::memset(source->buf[0]->data, 0xA5, source->buf[0]->size);
    std::memset(output->buf[0]->data, 0xB6, output->buf[0]->size);
    Fill444(source.get(), 0x11223344);
    const auto sourceBytes = std::vector<uint8_t>(source->buf[0]->data, source->buf[0]->data + source->buf[0]->size);
    const auto outputBytes = std::vector<uint8_t>(output->buf[0]->data, output->buf[0]->data + output->buf[0]->size);
    const auto layer = Layer(37, 19, 2);
    auto rejected = 0;
    const auto reject = [&](const AVFrame *input, AVFrame *destination)
    {
        auto checks = 0;
        Reject([&]
        {
            metal.Composite(input, layer, 203, destination, [&] { ++checks; });
        }, "Overlapping or overflowed Metal frame ranges were accepted");
        Require(checks == 0, "Invalid frame memory reached GPU composition checkpoints");
        Require(std::memcmp(source->buf[0]->data, sourceBytes.data(), sourceBytes.size()) == 0,
            "Rejected Metal alias changed source pixels or padding");
        Require(std::memcmp(output->buf[0]->data, outputBytes.data(), outputBytes.size()) == 0,
            "Rejected Metal alias changed output pixels or padding");
        ++rejected;
    };

    auto shallow = *source;
    reject(source.get(), &shallow);
    Frame cloned(av_frame_clone(source.get()));
    Require(static_cast<bool>(cloned), "Clone alias fixture allocation failed");
    reject(source.get(), cloned.get());
    auto reversed = *source;
    for (auto plane = 0; plane < 3; ++plane)
    {
        reversed.data[plane] += static_cast<ptrdiff_t>(reversed.height - 1) * reversed.linesize[plane];
        reversed.linesize[plane] = -reversed.linesize[plane];
    }
    reject(source.get(), &reversed);
    reject(&reversed, &shallow);
    auto crossPlane = *output;
    crossPlane.data[0] = source->data[2] + 2;
    crossPlane.linesize[0] = source->linesize[2];
    reject(source.get(), &crossPlane);
    crossPlane = *output;
    crossPlane.data[1] = source->data[0] + static_cast<ptrdiff_t>(source->height - 1) * source->linesize[0] + 4;
    crossPlane.linesize[1] = -source->linesize[0];
    reject(source.get(), &crossPlane);

    auto invalid = *source;
    invalid.data[0] = reinterpret_cast<uint8_t *>(std::numeric_limits<uintptr_t>::max() - 2);
    reject(&invalid, output.get());
    invalid.data[0] = reinterpret_cast<uint8_t *>(16);
    invalid.linesize[0] = -source->linesize[0];
    reject(&invalid, output.get());
    invalid = *source;
    const auto lastRowOffset = static_cast<uintptr_t>(source->height - 1) * source->linesize[0];
    invalid.data[0] = reinterpret_cast<uint8_t *>(std::numeric_limits<uintptr_t>::max() - lastRowOffset - 16);
    reject(&invalid, output.get());
    invalid = *output;
    invalid.data[2] = reinterpret_cast<uint8_t *>(std::numeric_limits<uintptr_t>::max() - 2);
    reject(source.get(), &invalid);
    std::cout << "PASS Metal frame isolation cases=" << rejected << " shallow_alias cloned_alias cross_plane negative_stride"
        << " pointer_overflow_underflow pixels_and_padding_unchanged\n";
}

void Validate()
{
    FrameRowExecutor rows(4);
    auto cases = 0;
    auto maximum = 0;
    uint64_t different = 0;
    for (const auto size : {std::pair{1, 1}, std::pair{37, 19}, std::pair{64, 33}})
    {
        MetalSdrCompositor metal(size.first, size.second);
        for (const auto range : {AVCOL_RANGE_MPEG, AVCOL_RANGE_JPEG})
        {
            for (const auto transfer : {AVCOL_TRC_BT709, AVCOL_TRC_SMPTE170M, AVCOL_TRC_IEC61966_2_1})
            {
                auto input = Allocate(size.first, size.second, AV_PIX_FMT_YUV444P16LE, range, transfer);
                auto expected = Allocate(size.first, size.second, AV_PIX_FMT_YUV444P16LE, range, transfer);
                auto actual = Allocate(size.first, size.second, AV_PIX_FMT_YUV444P16LE, range, transfer);
                Fill444(input.get(), 0x12345678);
                for (auto mode = 0; mode < 4; ++mode)
                {
                    const auto layer = Layer(size.first, size.second, mode);
                    for (const auto white : {100.0, 203.0, 1000.0})
                    {
                        CompositeCpu(input.get(), layer, white, expected.get(), rows);
                        metal.Composite(input.get(), layer, white, actual.get(), [] {});
                        const auto error = Compare(expected.get(), actual.get());
                        maximum = std::max(maximum, error.maximum);
                        different += error.different;
                        if (!mode)
                        {
                            Require(error.different == 0, "Transparent GPU foreground changed background pixels");
                        }
                        ++cases;
                    }
                }
                auto reversed = *input;
                for (auto plane = 0; plane < 3; ++plane)
                {
                    reversed.data[plane] += static_cast<ptrdiff_t>(reversed.height - 1) * reversed.linesize[plane];
                    reversed.linesize[plane] = -reversed.linesize[plane];
                }
                const auto layer = Layer(size.first, size.second, 3);
                CompositeCpu(&reversed, layer, 203, expected.get(), rows);
                metal.Composite(&reversed, layer, 203, actual.get(), [] {});
                const auto reversedError = Compare(expected.get(), actual.get());
                maximum = std::max(maximum, reversedError.maximum);
                different += reversedError.different;
                auto reversedOutput = *expected;
                for (auto plane = 0; plane < 3; ++plane)
                {
                    reversedOutput.data[plane] += static_cast<ptrdiff_t>(reversedOutput.height - 1) * reversedOutput.linesize[plane];
                    reversedOutput.linesize[plane] = -reversedOutput.linesize[plane];
                }
                metal.Composite(&reversed, layer, 203, &reversedOutput, [] {});
                Require(Compare(actual.get(), &reversedOutput).different == 0, "Metal negative-stride output changed row ordering");
                ++cases;
            }
        }
    }

    MetalSdrCompositor metal(37, 19);
    auto input = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    auto actual = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    auto sentinel = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    Fill444(input.get(), 71);
    Fill444(actual.get(), 89);
    Fill444(sentinel.get(), 89);
    auto layer = Layer(37, 19, 2);
    const auto invoke = [&]
    {
        metal.Composite(input.get(), layer, 203, actual.get(), [] {});
    };
    for (const auto alpha : {-0.1f, -std::numeric_limits<float>::denorm_min(), 1.1f,
        std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity()})
    {
        layer[3] = alpha;
        Reject(invoke, "Invalid GPU alpha was accepted");
        Require(Compare(actual.get(), sentinel.get()).different == 0, "Failed GPU command published output pixels");
    }
    layer[3] = 0.5f;
    layer[0] = std::numeric_limits<float>::quiet_NaN();
    Reject(invoke, "Non-finite active GPU layer was accepted");
    layer[3] = std::numeric_limits<float>::denorm_min();
    Reject(invoke, "Subnormal active GPU alpha hid non-finite RGB");
    Require(Compare(actual.get(), sentinel.get()).different == 0, "Invalid GPU RGB published output pixels");
    layer[3] = -0.0f;
    metal.Composite(input.get(), layer, 203, actual.get(), [] {});
    Require(reinterpret_cast<const uint16_t *>(actual->data[0])[0] == reinterpret_cast<const uint16_t *>(input->data[0])[0],
        "Transparent NaN RGB did not preserve the current YUV skip semantics");
    layer = Layer(37, 19, 2);
    Fill444(actual.get(), 89);
    for (const auto transfer : {AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67, AVCOL_TRC_UNSPECIFIED})
    {
        input->color_trc = transfer;
        Reject(invoke, "HDR or unspecified transfer entered the SDR prototype");
    }
    input->color_trc = AVCOL_TRC_BT709;
    input->color_primaries = AVCOL_PRI_BT2020;
    Reject(invoke, "Unsupported GPU primaries were accepted");
    input->color_primaries = AVCOL_PRI_BT709;
    input->colorspace = AVCOL_SPC_BT2020_NCL;
    Reject(invoke, "Unsupported GPU matrix was accepted");
    input->colorspace = AVCOL_SPC_BT709;
    input->color_range = AVCOL_RANGE_UNSPECIFIED;
    Reject(invoke, "Unspecified GPU range was accepted");
    input->color_range = AVCOL_RANGE_MPEG;
    input->crop_left = 1;
    Reject(invoke, "Cropped GPU input was accepted without a visible view");
    input->crop_left = 0;
    Reject([&] { metal.Composite(input.get(), layer, 0, actual.get(), [] {}); }, "Invalid GPU white was accepted");
    Reject([&] { metal.Composite(input.get(), layer, 1e-300, actual.get(), [] {}); }, "Underflowed GPU white was accepted");
    Reject([&] { metal.Composite(input.get(), layer, 203, actual.get(), {}); }, "Empty GPU cancellation callback was accepted");
    Reject([&] { metal.Composite(input.get(), std::span<const float>(layer).first(4), 203, actual.get(), [] {}); },
        "Short GPU layer was accepted");
    Reject([&] { metal.Composite(input.get(), layer, 203, input.get(), [] {}); }, "Aliased GPU output was accepted");
    Reject([&] { MetalSdrCompositor invalid(0, 19); }, "Invalid GPU dimensions were accepted");

    const auto checksBeforeCompletion = 1 + 3 * ((input->height + 31) / 32) + 1;
    for (const auto stopAt : {1, 2, checksBeforeCompletion + 1})
    {
        auto checks = 0;
        auto cancelled = false;
        try
        {
            metal.Composite(input.get(), layer, 203, actual.get(), [&]
            {
                if (++checks == stopAt)
                {
                    throw std::runtime_error("prototype cancellation");
                }
            });
        }
        catch (const std::runtime_error &error)
        {
            cancelled = std::string(error.what()) == "prototype cancellation";
        }
        Require(cancelled, "GPU cancellation checkpoint was not observed");
        Require(Compare(actual.get(), sentinel.get()).different == 0, "Cancelled GPU operation published output pixels");
    }
    invoke();
    auto expected = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    CompositeCpu(input.get(), layer, 203, expected.get(), rows);
    const auto retried = Compare(actual.get(), expected.get());
    auto repeat = Allocate(37, 19, AV_PIX_FMT_YUV444P16LE);
    metal.Composite(input.get(), layer, 203, repeat.get(), [] {});
    Require(Compare(actual.get(), repeat.get()).different == 0, "GPU retry was not deterministic after draining cancelled work");
    maximum = std::max(maximum, retried.maximum);
    different += retried.different;

    auto statisticsA = Allocate(1, 1, AV_PIX_FMT_YUV444P16LE);
    auto statisticsB = Allocate(1, 1, AV_PIX_FMT_YUV444P16LE);
    for (auto plane = 0; plane < 3; ++plane)
    {
        reinterpret_cast<uint16_t *>(statisticsA->data[plane])[0] = 100;
        reinterpret_cast<uint16_t *>(statisticsB->data[plane])[0] = static_cast<uint16_t>(100 + plane);
    }
    const auto knownError = Compare(statisticsA.get(), statisticsB.get());
    Require(knownError.maximum == 2 && knownError.different == 2 && knownError.samples == 3 && knownError.squared == 5,
        "GPU error-report statistics did not account for all plane samples");
    std::cout << "PASS Metal SDR prototype cases=" << cases << " max_error_16bit=" << maximum
        << " different_16bit_samples=" << different << " activation_eligible=" << (different ? "false" : "true")
        << " transparent_exact negative_stride metadata_rejection invalid_samples transactional_cancel retry error_report\n";
}

Scaler MakeScaler(int width, int height, AVPixelFormat source, AVPixelFormat destination, bool fullRange)
{
    Scaler result(sws_alloc_context());
    if (!result)
    {
        throw std::bad_alloc();
    }
    const std::pair<const char *, int64_t> options[]{{"srcw", width}, {"srch", height}, {"src_format", source},
        {"dstw", width}, {"dsth", height}, {"dst_format", destination}, {"src_range", fullRange},
        {"dst_range", fullRange}, {"sws_flags", SWS_BILINEAR | SWS_ACCURATE_RND | SWS_BITEXACT}, {"threads", 4}};
    for (const auto &option : options)
    {
        Check(av_opt_set_int(result.get(), option.first, option.second, 0), "Configure prototype scaler");
    }
    int x = 0;
    int y = 0;
    Check(av_chroma_location_enum_to_pos(&x, &y, AVCHROMA_LOC_LEFT), "Prototype chroma location");
    Check(av_opt_set_int(result.get(), source == AV_PIX_FMT_YUV420P ? "src_h_chr_pos" : "dst_h_chr_pos", x, 0), "Prototype chroma X");
    Check(av_opt_set_int(result.get(), source == AV_PIX_FMT_YUV420P ? "src_v_chr_pos" : "dst_v_chr_pos", y, 0), "Prototype chroma Y");
    Check(sws_init_context(result.get(), nullptr, nullptr), "Initialize prototype scaler");
    return result;
}

double Milliseconds(Clock::time_point begin)
{
    return std::chrono::duration<double, std::milli>(Clock::now() - begin).count();
}

double Median(std::vector<double> samples)
{
    std::sort(samples.begin(), samples.end());
    const auto middle = samples.size() / 2;
    return samples.size() % 2 ? samples[middle] : (samples[middle - 1] + samples[middle]) / 2;
}

void Benchmark(int width, int height, int iterations, bool extended)
{
    MetalSdrCompositor metal(width, height);
    FrameRowExecutor rows(4);
    auto source = Allocate(width, height, AV_PIX_FMT_YUV420P);
    auto intermediate = Allocate(width, height, AV_PIX_FMT_YUV444P16LE);
    auto cpu = Allocate(width, height, AV_PIX_FMT_YUV444P16LE);
    auto gpu = Allocate(width, height, AV_PIX_FMT_YUV444P16LE);
    auto cpuOutput = Allocate(width, height, AV_PIX_FMT_YUV420P);
    auto gpuOutput = Allocate(width, height, AV_PIX_FMT_YUV420P);
    auto upsample = MakeScaler(width, height, AV_PIX_FMT_YUV420P, AV_PIX_FMT_YUV444P16LE, false);
    auto downsample = MakeScaler(width, height, AV_PIX_FMT_YUV444P16LE, AV_PIX_FMT_YUV420P, false);
    auto layer = Layer(width, height, 2);
    if (!extended)
    {
        for (size_t pixel = 0; pixel < layer.size(); pixel += 4)
        {
            layer[pixel] = 0.3f;
            layer[pixel + 1] = 0.4f;
            layer[pixel + 2] = 0.1f;
        }
    }
    std::vector<double> cpuMilliseconds;
    std::vector<double> cpuCompositeMilliseconds;
    std::vector<double> gpuMilliseconds;
    std::vector<double> uploadMilliseconds;
    std::vector<double> dispatchMilliseconds;
    std::vector<double> deviceMilliseconds;
    std::vector<double> readbackMilliseconds;
    std::vector<double> gpuCompositeMilliseconds;
    ErrorStatistics errors444{};
    ErrorStatistics errors420{};
    for (auto iteration = -1; iteration < iterations; ++iteration)
    {
        Fill420(source.get(), static_cast<uint32_t>(iteration + 13));
        double cpuTotal = 0;
        double cpuComposite = 0;
        double gpuTotal = 0;
        MetalCompositorTiming timing{};
        const auto runCpu = [&]
        {
            const auto begin = Clock::now();
            Check(sws_scale_frame(upsample.get(), intermediate.get(), source.get()), "CPU prototype upsample");
            const auto compositeBegin = Clock::now();
            CompositeCpu(intermediate.get(), layer, 203, cpu.get(), rows);
            cpuComposite = Milliseconds(compositeBegin);
            Check(sws_scale_frame(downsample.get(), cpuOutput.get(), cpu.get()), "CPU prototype downsample");
            cpuTotal = Milliseconds(begin);
        };
        const auto runGpu = [&]
        {
            const auto begin = Clock::now();
            Check(sws_scale_frame(upsample.get(), intermediate.get(), source.get()), "GPU prototype upsample");
            timing = metal.Composite(intermediate.get(), layer, 203, gpu.get(), [] {});
            Check(sws_scale_frame(downsample.get(), gpuOutput.get(), gpu.get()), "GPU prototype downsample");
            gpuTotal = Milliseconds(begin);
        };
        if (iteration % 2)
        {
            runCpu();
            runGpu();
        }
        else
        {
            runGpu();
            runCpu();
        }
        const auto error444 = Compare(cpu.get(), gpu.get());
        const auto error420 = Compare(cpuOutput.get(), gpuOutput.get());
        if (iteration < 0)
        {
            continue;
        }
        errors444.maximum = std::max(errors444.maximum, error444.maximum);
        errors444.different += error444.different;
        errors444.samples += error444.samples;
        errors444.squared += error444.squared;
        errors420.maximum = std::max(errors420.maximum, error420.maximum);
        errors420.different += error420.different;
        errors420.samples += error420.samples;
        errors420.squared += error420.squared;
        cpuMilliseconds.push_back(cpuTotal);
        cpuCompositeMilliseconds.push_back(cpuComposite);
        gpuMilliseconds.push_back(gpuTotal);
        uploadMilliseconds.push_back(timing.uploadMilliseconds);
        dispatchMilliseconds.push_back(timing.dispatchAndWaitMilliseconds);
        deviceMilliseconds.push_back(timing.gpuMilliseconds);
        readbackMilliseconds.push_back(timing.readbackMilliseconds);
        gpuCompositeMilliseconds.push_back(timing.totalMilliseconds);
    }
    std::cout << "{\"prototype\":\"metal_sdr_dense\",\"extended_linear_rgb\":" << (extended ? "true" : "false")
        << ",\"device\":\"" << metal.DeviceName() << "\",\"width\":" << width
        << ",\"height\":" << height << ",\"iterations\":" << iterations << ",\"cpu_threads\":4,\"scaler_threads\":4"
        << ",\"cpu_pipeline_ms\":" << Median(cpuMilliseconds) << ",\"gpu_pipeline_ms\":" << Median(gpuMilliseconds)
        << ",\"pipeline_speedup\":" << Median(cpuMilliseconds) / Median(gpuMilliseconds)
        << ",\"cpu_composite_ms\":" << Median(cpuCompositeMilliseconds)
        << ",\"gpu_composite_ms\":" << Median(gpuCompositeMilliseconds)
        << ",\"upload_ms\":" << Median(uploadMilliseconds) << ",\"dispatch_and_wait_ms\":" << Median(dispatchMilliseconds)
        << ",\"device_ms\":" << Median(deviceMilliseconds) << ",\"readback_ms\":" << Median(readbackMilliseconds)
        << ",\"max_error_16bit\":" << errors444.maximum << ",\"rms_error_16bit\":" << std::sqrt(errors444.squared / errors444.samples)
        << ",\"different_16bit_samples\":" << errors444.different << ",\"samples_16bit\":" << errors444.samples
        << ",\"max_error_8bit\":" << errors420.maximum << ",\"rms_error_8bit\":" << std::sqrt(errors420.squared / errors420.samples)
        << ",\"different_8bit_samples\":" << errors420.different << ",\"samples_8bit\":" << errors420.samples
        << ",\"activation_eligible\":" << (errors444.different || errors420.different ? "false" : "true") << "}\n";
}
}

int main(int argc, char **argv)
{
    try
    {
        if (!MetalSdrCompositor::IsAvailable())
        {
            std::cout << "SKIP Metal prototype requires an Apple-family shared-memory GPU\n";
            return 77;
        }
        Validate();
        ValidateAliasing();
        if (argc > 1)
        {
            Require(std::string(argv[1]) == "--benchmark" && argc <= 3, "Usage: aeginext_export_metal_tests [--benchmark [iterations]]");
            const auto iterations = argc == 3 ? std::stoi(argv[2]) : 12;
            Require(iterations >= 3 && iterations <= 200, "Benchmark iterations must be between 3 and 200");
            Benchmark(1920, 1080, iterations, false);
            Benchmark(3840, 2160, iterations, false);
            Benchmark(1920, 1080, iterations, true);
            Benchmark(3840, 2160, iterations, true);
        }
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
