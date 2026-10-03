#include "aeginext_hdr.h"
#include "frame_conversion.h"
#include "hdr_error.h"

#include <array>
#include <cmath>
#include <cstddef>
#include <cstdio>
#include <limits>
#include <stdexcept>
#include <thread>
#include <vector>

namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

template<typename Action>
void RequireInvalid(Action &&action)
{
    try
    {
        action();
    }
    catch (const AegiNext::HdrError &exception)
    {
        Require(exception.Code() == AN_HDR_INVALID_ARGUMENT, "Expected INVALID_ARGUMENT.");
        return;
    }

    throw std::runtime_error("Invalid input was accepted.");
}

void HalfRepresentation()
{
    Require(AegiNext::HalfToFloat(0x3c00) == 1.0f, "Half 1.0 conversion failed.");
    Require(AegiNext::HalfToFloat(0xc400) == -4.0f, "Negative HDR half conversion failed.");
    Require(AegiNext::HalfToFloat(0x7bff) == 65504.0f, "Maximum finite half conversion failed.");
    Require(AegiNext::HalfToFloat(0x0400) == 0.00006103515625f, "Minimum normal half conversion failed.");
    Require(AegiNext::HalfToFloat(0x0001) == 0.000000059604644775390625f, "Half subnormal conversion failed.");
    Require(std::signbit(AegiNext::HalfToFloat(0x8000)), "Negative zero was lost.");
    Require(std::isinf(AegiNext::HalfToFloat(0x7c00)), "Half infinity decoding failed.");
    Require(std::isnan(AegiNext::HalfToFloat(0x7e00)), "Half NaN decoding failed.");
}

void ReferenceWhiteAndPremultiplication()
{
    const std::array<uint16_t, 4> first{0x3c00, 0x3800, 0xb400, 0x3800};
    const std::array<uint16_t, 4> second{0x4000, 0x3c00, 0xb800, 0x3800};
    std::vector<float> firstOutput;
    std::vector<float> secondOutput;
    AegiNext::NormalizeFrame(first.data(), sizeof(first), 1, 1, 8, 203, 406, firstOutput);
    AegiNext::NormalizeFrame(second.data(), sizeof(second), 1, 1, 8, 101.5f, 406, secondOutput);
    Require(firstOutput == secondOutput, "Equivalent luminance scales produced different pixels.");
    Require(firstOutput[0] == 1 && firstOutput[1] == 0.5f && firstOutput[2] == -0.25f,
            "Linear HDR or negative RGB was changed unexpectedly.");
    Require(firstOutput[3] == 0.5f, "Reference white conversion changed alpha.");
}

void PaddedNonSquareRows()
{
    std::array<uint16_t, 32> pixels{};
    pixels.fill(0x7e00);
    for (size_t row = 0; row < 3; ++row)
    {
        const auto offset = row * 12;
        const std::array<uint16_t, 8> values
        {
            0x3c00, 0, 0, 0x3c00,
            0, 0x4000, 0, 0x3c00
        };
        for (size_t channel = 0; channel < values.size(); ++channel)
        {
            pixels[offset + channel] = values[channel];
        }
    }

    std::vector<float> converted;
    AegiNext::NormalizeFrame(pixels.data(), sizeof(pixels), 2, 3, 24, 203, 406, converted);
    Require(converted.size() == 24, "Non-square frame channel count is incorrect.");
    for (size_t row = 0; row < 3; ++row)
    {
        Require(converted[row * 8] == 1 && converted[row * 8 + 5] == 2,
                "Padded row addressing changed a pixel or read padding.");
    }
}

void InvalidFrameBoundaries()
{
    std::array<uint16_t, 4> pixels{0x4000, 0, 0, 0x3c00};
    std::vector<float> output;
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 7, 1, 1, 8, 203, 406, output); });
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 6, 203, 406, output); });
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 8, 203, 203, output); });
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 8, 0, 406, output); });
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 8,
        std::numeric_limits<float>::infinity(), 406, output); });
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, UINT32_MAX, 1, 8, 203, 406, output); });
    pixels[3] = 0;
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 8, 203, 406, output); });
    pixels[3] = 0x4000;
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 8, 203, 406, output); });
    pixels[3] = 0x3c00;
    pixels[0] = 0x7c00;
    RequireInvalid([&] { AegiNext::NormalizeFrame(pixels.data(), 8, 1, 1, 8, 203, 406, output); });
}

void AbiAndErrorBuffers()
{
    static_assert(sizeof(an_hdr_status) == 72);
    static_assert(sizeof(an_hdr_verification) == 32);
    static_assert(offsetof(an_hdr_status, submitted_frames) == 48);
    Require(an_hdr_abi_version() == 1, "ABI version is incorrect.");
    Require(an_hdr_live_contexts() == 0, "A context leaked before creation.");
    std::array<char, 128> error{};
    void *context = nullptr;
    auto code = an_hdr_create(&context, &context, error.data(), error.size());
    Require(code == AN_HDR_INVALID_ARGUMENT && context == nullptr && error[0] != '\0',
            "Aliased create output pointers were not rejected.");

    an_hdr_verification verification{};
    verification.struct_size = sizeof(verification) - 1;
    verification.abi_version = 1;
    char tinyError = 'x';
    code = an_hdr_verify(nullptr, &verification, &tinyError, 1);
    Require(code == AN_HDR_INVALID_ARGUMENT && tinyError == '\0', "One-byte error buffer is not terminated.");
    verification.struct_size = sizeof(verification);
    verification.abi_version = 2;
    code = an_hdr_verify(nullptr, &verification, nullptr, 0);
    Require(code == AN_HDR_INVALID_ARGUMENT, "An incompatible ABI version was accepted.");
    verification.abi_version = 1;
    code = an_hdr_verify(reinterpret_cast<void *>(1), &verification, error.data(), error.size());
    Require(code == AN_HDR_INVALID_ARGUMENT, "An invalid native handle was dereferenced or accepted.");
    an_hdr_destroy(nullptr);
    an_hdr_destroy(reinterpret_cast<void *>(1));
    Require(an_hdr_live_contexts() == 0, "Invalid handles changed the live context count.");
}

void MainThreadRequirement()
{
    int32_t result = AN_HDR_OK;
    std::thread worker([&]
    {
        void *context = nullptr;
        void *view = nullptr;
        std::array<char, 128> error{};
        result = an_hdr_create(&context, &view, error.data(), error.size());
    });
    worker.join();
    Require(result == AN_HDR_WRONG_THREAD, "Background-thread creation was not rejected.");
    Require(an_hdr_live_contexts() == 0, "Wrong-thread creation allocated a context.");
}
}

int main()
{
    @autoreleasepool
    {
        const std::array<void (*)(), 6> tests
        {
            HalfRepresentation,
            ReferenceWhiteAndPremultiplication,
            PaddedNonSquareRows,
            InvalidFrameBoundaries,
            AbiAndErrorBuffers,
            MainThreadRequirement
        };
        auto failures = 0;
        for (size_t index = 0; index < tests.size(); ++index)
        {
            try
            {
                tests[index]();
            }
            catch (const std::exception &exception)
            {
                std::fprintf(stderr, "Native contract test %zu failed: %s\n", index + 1, exception.what());
                ++failures;
            }
        }

        std::printf("Native contracts: %zu passed, %d failed. GPU verification is a separate an_hdr_verify call.\n",
                    tests.size() - failures, failures);
        return failures == 0 ? 0 : 1;
    }
}
