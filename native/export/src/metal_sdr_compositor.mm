#include "metal_sdr_compositor.h"
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

namespace aeginext::encode::experimental
{
struct MetalSdrCompositorState final
{
    int width;
    int height;
    size_t pixels;
    id<MTLDevice> device;
    id<MTLCommandQueue> queue;
    id<MTLComputePipelineState> pipeline;
    id<MTLBuffer> input;
    id<MTLBuffer> layer;
    id<MTLBuffer> output;
    id<MTLBuffer> error;
};

namespace
{
using Clock = std::chrono::steady_clock;

struct MetalCompositeParameters final
{
    uint32_t pixelCount;
    uint32_t srgb;
    float yOffset;
    float yScale;
    float uvScale;
    float referenceWhite;
};
static_assert(sizeof(MetalCompositeParameters) == 24);

constexpr auto KERNEL =
#include "metal_compositor_kernel.inc"
;

double Milliseconds(Clock::time_point begin)
{
    return std::chrono::duration<double, std::milli>(Clock::now() - begin).count();
}

std::runtime_error MetalError(const char *operation, NSError *error)
{
    return std::runtime_error(std::string(operation) + ": " +
        (error ? error.localizedDescription.UTF8String : "Metal returned no resource"));
}

void ValidateFrame(const AVFrame *frame, int width, int height)
{
    if (!frame || frame->format != AV_PIX_FMT_YUV444P16LE || frame->width != width || frame->height != height ||
        frame->crop_left || frame->crop_right || frame->crop_top || frame->crop_bottom)
    {
        throw std::invalid_argument("Metal prototype requires an uncropped YUV444P16LE frame with the configured dimensions");
    }
    for (auto plane = 0; plane < 3; ++plane)
    {
        if (!frame->data[plane] || std::abs(static_cast<int64_t>(frame->linesize[plane])) < static_cast<int64_t>(width) * 2)
        {
            throw std::invalid_argument("Metal prototype received an incomplete YUV plane");
        }
    }
}

std::pair<uintptr_t, uintptr_t> PlaneMemoryRange(const AVFrame *frame, int plane)
{
    const auto base = reinterpret_cast<uintptr_t>(frame->data[plane]);
    const auto maximum = std::numeric_limits<uintptr_t>::max();
    const auto rowDistance = static_cast<uint64_t>(frame->height - 1) *
        static_cast<uint64_t>(std::abs(static_cast<int64_t>(frame->linesize[plane])));
    if (rowDistance > maximum)
    {
        throw std::invalid_argument("Metal prototype plane stride exceeds the address domain");
    }
    auto lowest = base;
    auto highest = base;
    if (frame->linesize[plane] < 0)
    {
        if (rowDistance > base)
        {
            throw std::invalid_argument("Metal prototype negative plane stride underflows its address range");
        }
        lowest -= static_cast<uintptr_t>(rowDistance);
    }
    else
    {
        if (rowDistance > maximum - base)
        {
            throw std::invalid_argument("Metal prototype plane stride overflows its address range");
        }
        highest += static_cast<uintptr_t>(rowDistance);
    }
    const auto rowBytes = static_cast<uint64_t>(frame->width) * sizeof(uint16_t);
    if (rowBytes > maximum - highest)
    {
        throw std::invalid_argument("Metal prototype plane row overflows its address range");
    }
    return {lowest, highest + static_cast<uintptr_t>(rowBytes)};
}

void ValidateSeparateMemory(const AVFrame *source, const AVFrame *output)
{
    const auto inputs = std::array{PlaneMemoryRange(source, 0), PlaneMemoryRange(source, 1), PlaneMemoryRange(source, 2)};
    const auto outputs = std::array{PlaneMemoryRange(output, 0), PlaneMemoryRange(output, 1), PlaneMemoryRange(output, 2)};
    for (const auto &input : inputs)
    {
        for (const auto &destination : outputs)
        {
            if (input.first < destination.second && destination.first < input.second)
            {
                throw std::invalid_argument("Metal prototype requires non-overlapping source and output plane memory");
            }
        }
    }
}
}

bool MetalSdrCompositor::IsAvailable()
{
    @autoreleasepool
    {
        const auto device = MTLCreateSystemDefaultDevice();
        return device && device.hasUnifiedMemory && [device supportsFamily:MTLGPUFamilyApple1];
    }
}

MetalSdrCompositor::MetalSdrCompositor(int width, int height)
{
    @autoreleasepool
    {
        if (width <= 0 || height <= 0 || static_cast<uint64_t>(width) * height > std::numeric_limits<uint32_t>::max())
        {
            throw std::invalid_argument("Metal prototype dimensions exceed its index domain");
        }
        auto state = std::make_unique<MetalSdrCompositorState>();
        state->width = width;
        state->height = height;
        state->pixels = static_cast<size_t>(width) * height;
        state->device = MTLCreateSystemDefaultDevice();
        if (!state->device || !state->device.hasUnifiedMemory || ![state->device supportsFamily:MTLGPUFamilyApple1])
        {
            throw std::runtime_error("Metal prototype requires an Apple-family shared-memory GPU");
        }
        if (state->pixels > state->device.maxBufferLength / (4 * sizeof(float)))
        {
            throw std::invalid_argument("Metal prototype frame exceeds the device buffer limit");
        }
        state->queue = [state->device newCommandQueue];
        auto options = [MTLCompileOptions new];
        if (@available(macOS 15.0, *))
        {
            options.mathMode = MTLMathModeSafe;
            options.mathFloatingPointFunctions = MTLMathFloatingPointFunctionsPrecise;
        }
        else
        {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
            options.fastMathEnabled = NO;
#pragma clang diagnostic pop
        }
        NSError *error = nil;
        const auto library = [state->device newLibraryWithSource:[NSString stringWithUTF8String:KERNEL]
            options:options error:&error];
        if (!library)
        {
            throw MetalError("Compile Metal SDR kernel", error);
        }
        const auto function = [library newFunctionWithName:@"CompositeSdr"];
        state->pipeline = [state->device newComputePipelineStateWithFunction:function error:&error];
        if (!state->pipeline)
        {
            throw MetalError("Create Metal SDR pipeline", error);
        }
        state->input = [state->device newBufferWithLength:state->pixels * 3 * sizeof(uint16_t) options:MTLResourceStorageModeShared];
        state->layer = [state->device newBufferWithLength:state->pixels * 4 * sizeof(float) options:MTLResourceStorageModeShared];
        state->output = [state->device newBufferWithLength:state->pixels * 3 * sizeof(uint16_t) options:MTLResourceStorageModeShared];
        state->error = [state->device newBufferWithLength:sizeof(uint32_t) options:MTLResourceStorageModeShared];
        if (!state->queue || !state->input || !state->layer || !state->output || !state->error)
        {
            throw std::bad_alloc();
        }
        state_ = std::move(state);
    }
}

MetalSdrCompositor::~MetalSdrCompositor() = default;

std::string MetalSdrCompositor::DeviceName() const
{
    return state_->device.name.UTF8String;
}

MetalCompositorTiming MetalSdrCompositor::Composite(const AVFrame *source, std::span<const float> layer,
    double referenceWhite, AVFrame *output, const std::function<void()> &checkCancel)
{
    @autoreleasepool
    {
        const auto begin = Clock::now();
        ValidateFrame(source, state_->width, state_->height);
        ValidateFrame(output, state_->width, state_->height);
        if (source == output || layer.size() < state_->pixels * 4 || !checkCancel || !std::isfinite(referenceWhite) ||
            referenceWhite <= 0 || referenceWhite > std::numeric_limits<float>::max() || static_cast<float>(referenceWhite) <= 0)
        {
            throw std::invalid_argument("Metal prototype requires a separate output, complete layer, cancellation callback and positive float reference white");
        }
        ValidateSeparateMemory(source, output);
        if (source->colorspace != AVCOL_SPC_BT709 || source->color_primaries != AVCOL_PRI_BT709 ||
            (source->color_trc != AVCOL_TRC_BT709 && source->color_trc != AVCOL_TRC_SMPTE170M &&
                source->color_trc != AVCOL_TRC_IEC61966_2_1) ||
            (source->color_range != AVCOL_RANGE_MPEG && source->color_range != AVCOL_RANGE_JPEG))
        {
            throw std::invalid_argument("Metal prototype supports explicit BT709 SDR metadata only; HDR is not supported");
        }
        checkCancel();
        MetalCompositorTiming timing{};
        auto stage = Clock::now();
        auto *packed = static_cast<uint16_t *>(state_->input.contents);
        for (auto plane = 0; plane < 3; ++plane)
        {
            for (auto y = 0; y < state_->height; ++y)
            {
                if (!(y % 32))
                {
                    checkCancel();
                }
                std::memcpy(packed + plane * state_->pixels + static_cast<size_t>(y) * state_->width,
                    source->data[plane] + static_cast<ptrdiff_t>(y) * source->linesize[plane], state_->width * sizeof(uint16_t));
            }
        }
        std::memcpy(state_->layer.contents, layer.data(), state_->pixels * 4 * sizeof(float));
        *static_cast<uint32_t *>(state_->error.contents) = 0;
        timing.uploadMilliseconds = Milliseconds(stage);
        checkCancel();
        const auto limited = source->color_range == AVCOL_RANGE_MPEG;
        const MetalCompositeParameters parameters{static_cast<uint32_t>(state_->pixels),
            source->color_trc == AVCOL_TRC_IEC61966_2_1 ? 1u : 0u, limited ? 4096.0f : 0.0f,
            limited ? 56064.0f : 65535.0f, limited ? 57344.0f : 65535.0f, static_cast<float>(referenceWhite)};
        stage = Clock::now();
        const auto command = [state_->queue commandBuffer];
        const auto encoder = [command computeCommandEncoder];
        if (!command || !encoder)
        {
            throw std::bad_alloc();
        }
        [encoder setComputePipelineState:state_->pipeline];
        [encoder setBuffer:state_->input offset:0 atIndex:0];
        [encoder setBuffer:state_->layer offset:0 atIndex:1];
        [encoder setBuffer:state_->output offset:0 atIndex:2];
        [encoder setBytes:&parameters length:sizeof(parameters) atIndex:3];
        [encoder setBuffer:state_->error offset:0 atIndex:4];
        const auto threads = std::min<NSUInteger>(256, state_->pipeline.maxTotalThreadsPerThreadgroup);
        [encoder dispatchThreads:MTLSizeMake(state_->pixels, 1, 1) threadsPerThreadgroup:MTLSizeMake(threads, 1, 1)];
        [encoder endEncoding];
        [command commit];
        [command waitUntilCompleted];
        timing.dispatchAndWaitMilliseconds = Milliseconds(stage);
        timing.gpuMilliseconds = (command.GPUEndTime - command.GPUStartTime) * 1000;
        if (command.status != MTLCommandBufferStatusCompleted)
        {
            throw MetalError("Execute Metal SDR kernel", command.error);
        }
        checkCancel();
        if (*static_cast<uint32_t *>(state_->error.contents))
        {
            throw std::invalid_argument("Metal prototype rejected a non-finite composited sample or invalid subtitle opacity");
        }
        if (av_frame_make_writable(output) < 0)
        {
            throw std::bad_alloc();
        }
        stage = Clock::now();
        const auto *result = static_cast<const uint16_t *>(state_->output.contents);
        for (auto plane = 0; plane < 3; ++plane)
        {
            for (auto y = 0; y < state_->height; ++y)
            {
                std::memcpy(output->data[plane] + static_cast<ptrdiff_t>(y) * output->linesize[plane],
                    result + plane * state_->pixels + static_cast<size_t>(y) * state_->width, state_->width * sizeof(uint16_t));
            }
        }
        timing.readbackMilliseconds = Milliseconds(stage);
        timing.totalMilliseconds = Milliseconds(begin);
        return timing;
    }
}
}
