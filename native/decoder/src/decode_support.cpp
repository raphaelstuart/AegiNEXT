#include "decode_support.h"
#include "decode_versions.h"
#include <algorithm>
#include <cstring>

extern "C"
{
#include <libavutil/avutil.h>
}

namespace aeginext::decode
{
static_assert(LIBAVFORMAT_VERSION_INT == AN_EXPECTED_AVFORMAT);
static_assert(LIBAVCODEC_VERSION_INT == AN_EXPECTED_AVCODEC);
static_assert(LIBAVUTIL_VERSION_INT == AN_EXPECTED_AVUTIL);

std::string AvError(int code)
{
    char message[AV_ERROR_MAX_STRING_SIZE]{};
    av_strerror(code, message, sizeof(message));
    return message;
}

void CheckAv(int code, int32_t result, const char *operation)
{
    if (code < 0)
    {
        throw Error(result, std::string(operation) + ": " + AvError(code));
    }
}

void CopyText(char *destination, uint32_t capacity, const char *text) noexcept
{
    if (!destination || capacity == 0)
    {
        return;
    }

    if (!text)
    {
        destination[0] = '\0';
        return;
    }

    const auto length = std::strlen(text);
    auto count = std::min<size_t>(length, capacity - 1);
    if (count < length)
    {
        while (count > 0 && (static_cast<unsigned char>(text[count]) & 0xc0) == 0x80)
        {
            --count;
        }
    }

    std::memcpy(destination, text, count);
    destination[count] = '\0';
}

void CopyName(char *destination, uint32_t capacity, const char *text)
{
    if (text && std::strlen(text) >= capacity)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Metadata name exceeds the ABI capacity.");
    }

    CopyText(destination, capacity, text);
}

an_decode_backend_info BackendInfo()
{
    an_decode_backend_info result{};
    result.struct_size = sizeof(result);
    result.abi_version = AN_DECODE_ABI_VERSION;
    result.compile_avformat = LIBAVFORMAT_VERSION_INT;
    result.runtime_avformat = avformat_version();
    result.compile_avcodec = LIBAVCODEC_VERSION_INT;
    result.runtime_avcodec = avcodec_version();
    result.compile_avutil = LIBAVUTIL_VERSION_INT;
    result.runtime_avutil = avutil_version();
    CopyName(result.release_version, sizeof(result.release_version), av_version_info());
    return result;
}

void ValidateBackend()
{
    const auto info = BackendInfo();
    if (info.runtime_avformat != AN_EXPECTED_AVFORMAT ||
        info.runtime_avcodec != AN_EXPECTED_AVCODEC ||
        info.runtime_avutil != AN_EXPECTED_AVUTIL)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "FFmpeg runtime libraries do not match the pinned development headers.");
    }

    for (const auto *accepted : AN_ACCEPTED_RELEASES)
    {
        if (std::strcmp(info.release_version, accepted) == 0)
        {
            return;
        }
    }

    throw Error(AN_DECODE_UNSUPPORTED, "FFmpeg release is not in the checked-in toolchain manifest.");
}
}
