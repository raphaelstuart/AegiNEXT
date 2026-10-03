#pragma once

#include "frame_owner.h"

namespace aeginext::decode
{
class DecoderContext final
{
public:
    DecoderContext();
    ~DecoderContext();
    DecoderContext(const DecoderContext &) = delete;
    DecoderContext &operator=(const DecoderContext &) = delete;
    void Open(const char *path, int32_t streamIndex);
    std::unique_ptr<FrameOwner> ReadNext();
    an_decode_ratio StreamTimeBase() const;
    void Seek(int64_t timestamp);
    void Cancel() noexcept;

private:
    static int Interrupt(void *opaque) noexcept;
    static AVPixelFormat SoftwareFormat(AVCodecContext *, const AVPixelFormat *formats) noexcept;
    void CheckCancelled() const;
    void Close() noexcept;
    std::unique_ptr<FrameOwner> ReadNextInternal();
    std::atomic<bool> cancelled_{false};
    bool openAttempted_ = false;
    bool ready_ = false;
    bool failed_ = false;
    bool packetPending_ = false;
    bool demuxEof_ = false;
    bool drainSent_ = false;
    bool decoderEof_ = false;
    int32_t streamIndex_ = -1;
    AVRational streamTimeBase_{};
    AVFormatContext *format_ = nullptr;
    AVCodecContext *codec_ = nullptr;
    AVPacket *packet_ = nullptr;
    FramePointer scratch_;
};
}
