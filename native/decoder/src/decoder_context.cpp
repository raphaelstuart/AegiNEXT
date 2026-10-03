#include "decoder_context.h"
#include <cstring>
#include <filesystem>

namespace aeginext::decode
{
DecoderContext::DecoderContext()
{
    ValidateBackend();
}

DecoderContext::~DecoderContext()
{
    Close();
}

void DecoderContext::Close() noexcept
{
    scratch_.reset();
    av_packet_free(&packet_);
    avcodec_free_context(&codec_);
    avformat_close_input(&format_);
    ready_ = false;
}

void DecoderContext::Cancel() noexcept
{
    cancelled_.store(true, std::memory_order_release);
}

int DecoderContext::Interrupt(void *opaque) noexcept
{
    return static_cast<DecoderContext *>(opaque)->cancelled_.load(std::memory_order_acquire) ? 1 : 0;
}

void DecoderContext::CheckCancelled() const
{
    if (cancelled_.load(std::memory_order_acquire))
    {
        throw Error(AN_DECODE_CANCELLED, "Decoding was cancelled; this decoder cannot be resumed.");
    }
}

AVPixelFormat DecoderContext::SoftwareFormat(AVCodecContext *, const AVPixelFormat *formats) noexcept
{
    for (const auto *format = formats; *format != AV_PIX_FMT_NONE; ++format)
    {
        const auto *descriptor = av_pix_fmt_desc_get(*format);
        if (descriptor && (descriptor->flags & AV_PIX_FMT_FLAG_HWACCEL) == 0)
        {
            return *format;
        }
    }

    return AV_PIX_FMT_NONE;
}

void DecoderContext::Open(const char *path, int32_t streamIndex)
{
    CheckCancelled();
    if (openAttempted_)
    {
        throw Error(AN_DECODE_INVALID_STATE, "A decoder can be opened only once.");
    }

    openAttempted_ = true;
    try
    {
        if (!path || !*path || streamIndex < 0)
        {
            throw Error(AN_DECODE_INVALID_ARGUMENT, "An absolute local path and nonnegative video stream index are required.");
        }

        const std::u8string utf8Path(path, path + std::strlen(path));
        const std::filesystem::path localPath(utf8Path);
        if (!localPath.is_absolute())
        {
            throw Error(AN_DECODE_INVALID_ARGUMENT, "Only absolute local media paths are accepted.");
        }

        std::error_code fileError;
        if (!std::filesystem::is_regular_file(localPath, fileError))
        {
            throw Error(AN_DECODE_IO_ERROR, "Input is not a readable regular local media file.");
        }

        format_ = avformat_alloc_context();
        if (!format_)
        {
            throw std::bad_alloc();
        }

        format_->interrupt_callback = {Interrupt, this};
        AVDictionary *options = nullptr;
        const auto optionResult = av_dict_set(&options, "protocol_whitelist", "file", 0);
        if (optionResult < 0)
        {
            av_dict_free(&options);
            CheckAv(optionResult, AN_DECODE_NATIVE_FAILURE, "protocol_whitelist");
        }

        const auto openResult = avformat_open_input(&format_, path, nullptr, &options);
        av_dict_free(&options);
        CheckCancelled();
        CheckAv(openResult, AN_DECODE_IO_ERROR, "avformat_open_input");
        const auto streamResult = avformat_find_stream_info(format_, nullptr);
        CheckCancelled();
        CheckAv(streamResult, AN_DECODE_IO_ERROR, "avformat_find_stream_info");
        if (static_cast<uint32_t>(streamIndex) >= format_->nb_streams ||
            format_->streams[streamIndex]->index != streamIndex ||
            format_->streams[streamIndex]->codecpar->codec_type != AVMEDIA_TYPE_VIDEO)
        {
            throw Error(AN_DECODE_INVALID_ARGUMENT, "The requested stream index does not identify a video stream.");
        }

        const auto *stream = format_->streams[streamIndex];
        if (stream->time_base.num <= 0 || stream->time_base.den <= 0)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "The selected video stream has no valid time base.");
        }

        const auto *decoder = avcodec_find_decoder(stream->codecpar->codec_id);
        if (!decoder || (decoder->capabilities & AV_CODEC_CAP_HARDWARE) != 0)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "A software decoder is unavailable for the selected video stream.");
        }

        codec_ = avcodec_alloc_context3(decoder);
        if (!codec_)
        {
            throw std::bad_alloc();
        }

        CheckAv(avcodec_parameters_to_context(codec_, stream->codecpar), AN_DECODE_NATIVE_FAILURE,
            "avcodec_parameters_to_context");
        codec_->pkt_timebase = stream->time_base;
        codec_->get_format = SoftwareFormat;
        codec_->thread_count = 1;
        CheckAv(avcodec_open2(codec_, decoder, nullptr), AN_DECODE_DECODE_ERROR, "avcodec_open2");
        CheckCancelled();
        packet_ = av_packet_alloc();
        scratch_.reset(av_frame_alloc());
        if (!packet_ || !scratch_)
        {
            throw std::bad_alloc();
        }

        streamIndex_ = streamIndex;
        streamTimeBase_ = stream->time_base;
        ready_ = true;
    }
    catch (...)
    {
        failed_ = true;
        Close();
        throw;
    }
}

std::unique_ptr<FrameOwner> DecoderContext::ReadNext()
{
    CheckCancelled();
    if (!ready_ || failed_)
    {
        throw Error(AN_DECODE_INVALID_STATE, "Decoder is not open or a previous operation failed.");
    }

    if (decoderEof_)
    {
        return nullptr;
    }

    try
    {
        return ReadNextInternal();
    }
    catch (...)
    {
        failed_ = true;
        throw;
    }
}

an_decode_ratio DecoderContext::StreamTimeBase() const
{
    CheckCancelled();
    if (!ready_ || failed_)
    {
        throw Error(AN_DECODE_INVALID_STATE, "Decoder is not open or a previous operation failed.");
    }

    return {streamTimeBase_.num, streamTimeBase_.den};
}

void DecoderContext::Seek(int64_t timestamp)
{
    CheckCancelled();
    if (!ready_ || failed_)
    {
        throw Error(AN_DECODE_INVALID_STATE, "Decoder is not open or a previous operation failed.");
    }

    if (timestamp == AV_NOPTS_VALUE)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "A seek target cannot be the missing-timestamp sentinel INT64_MIN.");
    }

    try
    {
        const auto result = av_seek_frame(format_, streamIndex_, timestamp, AVSEEK_FLAG_BACKWARD);
        CheckCancelled();
        CheckAv(result, AN_DECODE_IO_ERROR, "av_seek_frame");
        avcodec_flush_buffers(codec_);
        av_packet_unref(packet_);
        av_frame_unref(scratch_.get());
        packetPending_ = false;
        demuxEof_ = false;
        drainSent_ = false;
        decoderEof_ = false;
        CheckCancelled();
    }
    catch (...)
    {
        failed_ = true;
        throw;
    }
}

std::unique_ptr<FrameOwner> DecoderContext::ReadNextInternal()
{
    while (true)
    {
        CheckCancelled();
        const auto receiveResult = avcodec_receive_frame(codec_, scratch_.get());
        CheckCancelled();
        if (receiveResult == 0)
        {
            FramePointer frame(av_frame_alloc());
            if (!frame)
            {
                throw std::bad_alloc();
            }

            av_frame_move_ref(frame.get(), scratch_.get());
            return std::make_unique<FrameOwner>(std::move(frame), streamTimeBase_);
        }

        if (receiveResult == AVERROR_EOF)
        {
            if (!drainSent_)
            {
                throw Error(AN_DECODE_DECODE_ERROR, "Decoder reached EOF before the demuxer was drained.");
            }

            decoderEof_ = true;
            return nullptr;
        }

        if (receiveResult != AVERROR(EAGAIN))
        {
            CheckAv(receiveResult, AN_DECODE_DECODE_ERROR, "avcodec_receive_frame");
        }

        if (packetPending_)
        {
            const auto sendResult = avcodec_send_packet(codec_, packet_);
            CheckCancelled();
            if (sendResult == AVERROR(EAGAIN))
            {
                throw Error(AN_DECODE_DECODE_ERROR, "Decoder returned EAGAIN on both receive and send without progress.");
            }

            CheckAv(sendResult, AN_DECODE_DECODE_ERROR, "avcodec_send_packet");
            av_packet_unref(packet_);
            packetPending_ = false;
            continue;
        }

        if (demuxEof_)
        {
            if (drainSent_)
            {
                throw Error(AN_DECODE_DECODE_ERROR, "Decoder requested more packets after accepting its drain packet.");
            }

            const auto drainResult = avcodec_send_packet(codec_, nullptr);
            CheckCancelled();
            CheckAv(drainResult, AN_DECODE_DECODE_ERROR, "avcodec_send_packet(drain)");
            drainSent_ = true;
            continue;
        }

        while (!packetPending_ && !demuxEof_)
        {
            CheckCancelled();
            const auto readResult = av_read_frame(format_, packet_);
            CheckCancelled();
            if (readResult == AVERROR_EOF)
            {
                demuxEof_ = true;
            }
            else
            {
                CheckAv(readResult, AN_DECODE_IO_ERROR, "av_read_frame");
                if (packet_->stream_index == streamIndex_)
                {
                    packetPending_ = true;
                }
                else
                {
                    av_packet_unref(packet_);
                }
            }
        }
    }
}
}
