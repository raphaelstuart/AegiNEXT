#pragma once

#include "aeginext_decode.h"
#include <atomic>
#include <memory>
#include <stdexcept>
#include <string>

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/frame.h>
}

namespace aeginext::decode
{
class Error final : public std::runtime_error
{
public:
    Error(int32_t result, const std::string &message) : std::runtime_error(message), result_(result) {}
    int32_t Result() const noexcept { return result_; }
private:
    int32_t result_;
};

struct FrameDeleter
{
    void operator()(AVFrame *frame) const noexcept { av_frame_free(&frame); }
};

using FramePointer = std::unique_ptr<AVFrame, FrameDeleter>;

std::string AvError(int code);
void CheckAv(int code, int32_t result, const char *operation);
void CopyText(char *destination, uint32_t capacity, const char *text) noexcept;
void CopyName(char *destination, uint32_t capacity, const char *text);
an_decode_backend_info BackendInfo();
void ValidateBackend();
}
