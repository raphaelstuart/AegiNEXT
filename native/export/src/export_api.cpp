#include "aeginext_export.h"
#include "color_pipeline.h"
#include "export_versions.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_set>
#include <vector>
extern "C"
{
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
#include <libswscale/swscale.h>
}
namespace
{
using aeginext::encode::ColorPipeline;
struct Failure : std::runtime_error
{
    int code;
    Failure(int c, const std::string &message) : std::runtime_error(message), code(c) {}
};
void Check(int result, const char *operation)
{
    if (result < 0)
    {
        char text[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, text, sizeof(text));
        throw Failure(3, std::string(operation) + ": " + text);
    }
}
void Need(bool valid, const char *message)
{
    if (!valid) throw Failure(2, message);
}
struct FrameDeleter { void operator()(AVFrame *p) const { av_frame_free(&p); } };
using Frame = std::unique_ptr<AVFrame, FrameDeleter>;
Frame Allocate(AVPixelFormat format, int width, int height)
{
    Frame f(av_frame_alloc());
    if (!f) throw std::bad_alloc();
    f->format = format; f->width = width; f->height = height;
    Check(av_frame_get_buffer(f.get(), 32), "frame buffer");
    return f;
}
struct Context
{
    std::atomic<bool> cancelled{false};
    bool started = false;
    AVFormatContext *input = nullptr, *output = nullptr;
    AVCodecContext *decoder = nullptr, *encoder = nullptr;
    AVPacket *packet = nullptr, *encoded = nullptr;
    SwsContext *upsample = nullptr, *downsample = nullptr;
    ~Context()
    {
        sws_free_context(&upsample); sws_free_context(&downsample);
        av_packet_free(&packet); av_packet_free(&encoded);
        avcodec_free_context(&decoder); avcodec_free_context(&encoder);
        avformat_close_input(&input);
        if (output)
        {
            if (output->pb) avio_closep(&output->pb);
            avformat_free_context(output);
        }
    }
    void CheckCancel() const { if (cancelled.load()) throw Failure(4, "Export cancelled"); }
};
std::mutex registryMutex;
std::unordered_set<Context *> registry;
Context *Get(void *handle)
{
    std::scoped_lock lock(registryMutex);
    auto *context = static_cast<Context *>(handle);
    if (!registry.contains(context)) throw Failure(1, "Invalid export context");
    return context;
}
int Interrupt(void *opaque) { return static_cast<Context *>(opaque)->cancelled.load() ? 1 : 0; }
void CopyError(char *output, uint32_t capacity, const char *text)
{
    if (!output || !capacity) return;
    const auto count = std::min<size_t>(capacity - 1, std::strlen(text));
    std::memcpy(output, text, count); output[count] = 0;
}
void Versions()
{
    static_assert(LIBAVFORMAT_VERSION_INT == AN_EXPORT_AVFORMAT && LIBAVCODEC_VERSION_INT == AN_EXPORT_AVCODEC &&
        LIBAVUTIL_VERSION_INT == AN_EXPORT_AVUTIL && LIBSWSCALE_VERSION_INT == AN_EXPORT_SWSCALE);
    Need(avformat_version() == AN_EXPORT_AVFORMAT && avcodec_version() == AN_EXPORT_AVCODEC &&
        avutil_version() == AN_EXPORT_AVUTIL && swscale_version() == AN_EXPORT_SWSCALE, "FFmpeg runtime does not match pinned export headers");
}
void ValidateFrame(const AVFrame *f)
{
    const auto *desc = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(f->format));
    Need(desc && desc->nb_components == 3 && !(desc->flags & (AV_PIX_FMT_FLAG_RGB | AV_PIX_FMT_FLAG_FLOAT |
        AV_PIX_FMT_FLAG_HWACCEL | AV_PIX_FMT_FLAG_ALPHA | AV_PIX_FMT_FLAG_PAL | AV_PIX_FMT_FLAG_BITSTREAM | AV_PIX_FMT_FLAG_BAYER)),
        "Export currently requires opaque integer YUV input");
    for (int i = 0; i < 3; ++i) Need(desc->comp[i].depth >= 8 && desc->comp[i].depth <= 16, "Unsupported input depth");
    Need(f->width > 0 && f->height > 0 && static_cast<uint64_t>(f->width) * f->height <= 33177600,
        "Export input exceeds coded pixel limit");
    Need(!(f->flags & (AV_FRAME_FLAG_INTERLACED | AV_FRAME_FLAG_CORRUPT)) && !f->decode_error_flags,
        "Interlaced or corrupt export frame is unsupported");
    Need(f->color_range == AVCOL_RANGE_MPEG || f->color_range == AVCOL_RANGE_JPEG, "Source range must be explicit");
    Need(std::strncmp(desc->name, "yuvj", 4) != 0 || f->color_range == AVCOL_RANGE_JPEG, "YUVJ format conflicts with limited range");
    Need(!(desc->log2_chroma_w || desc->log2_chroma_h) || f->chroma_location != AVCHROMA_LOC_UNSPECIFIED,
        "Subsampled export requires explicit chroma location");
    Need(f->sample_aspect_ratio.num == 0 || (f->sample_aspect_ratio.den > 0 && f->sample_aspect_ratio.num == f->sample_aspect_ratio.den),
        "First export version requires square source pixels; anamorphic subtitle geometry needs an explicit project policy");
    Need(f->crop_left < static_cast<size_t>(f->width) && f->crop_right < static_cast<size_t>(f->width) - f->crop_left &&
        f->crop_top < static_cast<size_t>(f->height) && f->crop_bottom < static_cast<size_t>(f->height) - f->crop_top, "Empty frame crop");
    for (int i = 0; i < f->nb_side_data; ++i)
    {
        switch (f->side_data[i]->type)
        {
        case AV_FRAME_DATA_DISPLAYMATRIX: case AV_FRAME_DATA_STEREO3D: case AV_FRAME_DATA_DYNAMIC_HDR_PLUS:
        case AV_FRAME_DATA_DOVI_RPU_BUFFER: case AV_FRAME_DATA_DOVI_METADATA: case AV_FRAME_DATA_DYNAMIC_HDR_VIVID:
        case AV_FRAME_DATA_DYNAMIC_HDR_SMPTE_2094_APP5: case AV_FRAME_DATA_ICC_PROFILE: case AV_FRAME_DATA_RAW_COLOR_PARAMS:
        case AV_FRAME_DATA_FILM_GRAIN_PARAMS: case AV_FRAME_DATA_AMBIENT_VIEWING_ENVIRONMENT:
            throw Failure(2, "Display transform, dynamic HDR, ICC or auxiliary color metadata requires an explicit export policy");
        default: break;
        }
    }
}
SwsContext *MakeScaler(int sw, int sh, AVPixelFormat sf, int dw, int dh, AVPixelFormat df, AVChromaLocation srcLocation, AVChromaLocation dstLocation, bool fullRange)
{
    auto *c = sws_alloc_context();
    if (!c) throw std::bad_alloc();
    try
    {
        Check(av_opt_set_int(c, "srcw", sw, 0), "srcw"); Check(av_opt_set_int(c, "srch", sh, 0), "srch");
        Check(av_opt_set_int(c, "src_format", sf, 0), "src format");
        Check(av_opt_set_int(c, "dstw", dw, 0), "dstw"); Check(av_opt_set_int(c, "dsth", dh, 0), "dsth");
        Check(av_opt_set_int(c, "dst_format", df, 0), "dst format");
        Check(av_opt_set_int(c, "src_range", fullRange, 0), "source range");
        Check(av_opt_set_int(c, "dst_range", fullRange, 0), "target range");
        Check(av_opt_set_int(c, "sws_flags", SWS_BILINEAR | SWS_ACCURATE_RND | SWS_BITEXACT, 0), "scale flags");
        for (int side = 0; side < 2; ++side)
        {
            const auto location = side ? dstLocation : srcLocation;
            if (location == AVCHROMA_LOC_UNSPECIFIED) continue;
            int x = 0, y = 0; Check(av_chroma_location_enum_to_pos(&x, &y, location), "chroma position");
            Check(av_opt_set_int(c, side ? "dst_h_chr_pos" : "src_h_chr_pos", x, 0), "chroma X");
            Check(av_opt_set_int(c, side ? "dst_v_chr_pos" : "src_v_chr_pos", y, 0), "chroma Y");
        }
        Check(sws_init_context(c, nullptr, nullptr), "initialize same-domain YUV resampling");
        return c;
    }
    catch (...) { sws_free_context(&c); throw; }
}
void WritePackets(Context &c, AVStream *stream)
{
    while (true)
    {
        c.CheckCancel();
        const auto result = avcodec_receive_packet(c.encoder, c.encoded);
        if (result == AVERROR(EAGAIN) || result == AVERROR_EOF) return;
        Check(result, "receive encoded packet");
        av_packet_rescale_ts(c.encoded, c.encoder->time_base, stream->time_base);
        c.encoded->stream_index = stream->index;
        Check(av_interleaved_write_frame(c.output, c.encoded), "mux encoded packet");
    }
}
std::string MasteringOption(const AVFrame *f)
{
    const auto *side = av_frame_get_side_data(f, AV_FRAME_DATA_MASTERING_DISPLAY_METADATA);
    if (!side) return {};
    Need(side->size >= sizeof(AVMasteringDisplayMetadata), "Truncated mastering metadata");
    const auto *m = reinterpret_cast<const AVMasteringDisplayMetadata *>(side->data);
    Need(m->has_primaries && m->has_luminance, "Partial mastering display metadata cannot be faithfully emitted by this encoder");
    auto q = [](AVRational value, int scale)
    {
        Need(value.den > 0 && value.num >= 0, "Invalid mastering rational");
        return std::llround(av_q2d(value) * scale);
    };
    Need(av_q2d(m->max_luminance) > av_q2d(m->min_luminance) && av_q2d(m->max_luminance) <= 10000,
        "Invalid mastering display luminance");
    for (int p = 0; p < 3; ++p)
        for (int axis = 0; axis < 2; ++axis)
            Need(m->display_primaries[p][axis].den > 0 && m->display_primaries[p][axis].num >= 0 &&
                av_q2d(m->display_primaries[p][axis]) <= 1, "Invalid mastering primary coordinate");
    for (int axis = 0; axis < 2; ++axis)
        Need(m->white_point[axis].den > 0 && m->white_point[axis].num >= 0 && av_q2d(m->white_point[axis]) <= 1,
            "Invalid mastering white coordinate");
    std::ostringstream s;
    for (const int p : {1, 2, 0}) s << "GBR"[p == 1 ? 0 : p == 2 ? 1 : 2] << '(' << q(m->display_primaries[p][0], 50000) << ',' << q(m->display_primaries[p][1], 50000) << ')';
    s << "WP(" << q(m->white_point[0], 50000) << ',' << q(m->white_point[1], 50000) << ")L(" << q(m->max_luminance, 10000) << ',' << q(m->min_luminance, 10000) << ')';
    return s.str();
}
void Execute(Context &c, const an_export_request &r, an_export_render_callback render, void *user, uint64_t &frames)
{
    Versions(); c.CheckCancel();
    if (c.started) throw Failure(1, "Export context may run only once");
    c.started = true;
    Need(!std::filesystem::exists(std::filesystem::path(reinterpret_cast<const char8_t *>(r.output_path))), "Temporary export output already exists");
    Need(std::filesystem::is_regular_file(std::filesystem::path(reinterpret_cast<const char8_t *>(r.input_path))), "Export input must be a local regular file");
    c.input = avformat_alloc_context();
    if (!c.input) throw std::bad_alloc();
    c.input->interrupt_callback = {Interrupt, &c};
    Check(avformat_open_input(&c.input, r.input_path, nullptr, nullptr), "open export input");
    Check(avformat_find_stream_info(c.input, nullptr), "inspect export input");
    Need(r.video_stream_index >= 0 && r.video_stream_index < static_cast<int>(c.input->nb_streams), "Selected video stream does not exist");
    auto *sourceStream = c.input->streams[r.video_stream_index];
    Need(sourceStream->codecpar->codec_type == AVMEDIA_TYPE_VIDEO && sourceStream->time_base.num > 0 && sourceStream->time_base.den > 0, "Invalid video stream/time base");
    for (int i = 0; i < sourceStream->codecpar->nb_coded_side_data; ++i)
    {
        switch (sourceStream->codecpar->coded_side_data[i].type)
        {
        case AV_PKT_DATA_DISPLAYMATRIX: case AV_PKT_DATA_STEREO3D: case AV_PKT_DATA_ICC_PROFILE:
        case AV_PKT_DATA_DOVI_CONF: case AV_PKT_DATA_DYNAMIC_HDR10_PLUS: case AV_PKT_DATA_DYNAMIC_HDR_SMPTE_2094_APP5:
        case AV_PKT_DATA_AMBIENT_VIEWING_ENVIRONMENT: case AV_PKT_DATA_3D_REFERENCE_DISPLAYS:
            throw Failure(2, "Stream display transform or auxiliary HDR/color metadata requires an explicit export policy");
        default: break;
        }
    }
    const auto *decoder = avcodec_find_decoder(sourceStream->codecpar->codec_id);
    Need(decoder != nullptr, "Video decoder unavailable");
    c.decoder = avcodec_alloc_context3(decoder);
    if (!c.decoder) throw std::bad_alloc();
    Check(avcodec_parameters_to_context(c.decoder, sourceStream->codecpar), "decoder parameters");
    c.decoder->thread_count = 4; c.decoder->pkt_timebase = sourceStream->time_base; c.decoder->apply_cropping = 0;
    Check(avcodec_open2(c.decoder, decoder, nullptr), "open software decoder");
    c.packet = av_packet_alloc(); c.encoded = av_packet_alloc();
    Frame decoded(av_frame_alloc());
    if (!c.packet || !c.encoded || !decoded) throw std::bad_alloc();
    Frame upsampled, composed, outputFrame;
    AVStream *targetStream = nullptr;
    std::unique_ptr<ColorPipeline> color;
    std::vector<float> layer(static_cast<size_t>(r.width) * r.height * 4);
    int64_t lastPts = AV_NOPTS_VALUE;
    std::string mastering;
    int sourceFormat = -1, sourceWidth = 0, sourceHeight = 0, matrix = -1, primaries = -1, transfer = -1, range = -1, chroma = -1;
    auto process = [&]()
    {
        c.CheckCancel(); ValidateFrame(decoded.get());
        const auto width = decoded->width - decoded->crop_left - decoded->crop_right;
        const auto height = decoded->height - decoded->crop_top - decoded->crop_bottom;
        Need(width == r.width && height == r.height, "Project canvas must equal visible video dimensions");
        const auto pts = decoded->pts != AV_NOPTS_VALUE ? decoded->pts : decoded->best_effort_timestamp;
        Need(pts != AV_NOPTS_VALUE && (lastPts == AV_NOPTS_VALUE || pts > lastPts), "Export requires known strictly increasing presentation timestamps");
        lastPts = pts;
        if (!c.encoder)
        {
            const bool hdr = decoded->color_trc == AVCOL_TRC_SMPTE2084 || decoded->color_trc == AVCOL_TRC_ARIB_STD_B67;
            const auto codec = r.codec == 0 ? (hdr ? 2 : 1) : r.codec;
            Need(!(hdr && codec == 1), "HDR requires HEVC 10-bit output");
            const auto *encoder = avcodec_find_encoder_by_name(codec == 1 ? "libx264" : "libx265");
            Need(encoder != nullptr, "Requested software encoder is unavailable");
            Check(avformat_alloc_output_context2(&c.output, nullptr, "nut", r.output_path), "create export container");
            c.output->avoid_negative_ts = AVFMT_AVOID_NEG_TS_DISABLED;
            c.output->interrupt_callback = {Interrupt, &c};
            c.encoder = avcodec_alloc_context3(encoder);
            if (!c.encoder) throw std::bad_alloc();
            c.encoder->width = r.width; c.encoder->height = r.height;
            c.encoder->pix_fmt = codec == 1 ? AV_PIX_FMT_YUV420P : AV_PIX_FMT_YUV420P10LE;
            c.encoder->time_base = sourceStream->time_base;
            c.encoder->framerate = av_guess_frame_rate(c.input, sourceStream, decoded.get());
            c.encoder->sample_aspect_ratio = decoded->sample_aspect_ratio;
            c.encoder->color_range = decoded->color_range; c.encoder->colorspace = decoded->colorspace;
            c.encoder->color_primaries = decoded->color_primaries; c.encoder->color_trc = decoded->color_trc;
            c.encoder->chroma_sample_location = AVCHROMA_LOC_LEFT;
            c.encoder->thread_count = 4; c.encoder->flags |= AV_CODEC_FLAG_FRAME_DURATION;
            if (c.output->oformat->flags & AVFMT_GLOBALHEADER) c.encoder->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
            AVDictionary *options = nullptr;
            av_dict_set(&options, "preset", r.preset, 0);
            av_dict_set(&options, "crf", std::to_string(r.crf).c_str(), 0);
            if (codec == 2)
            {
                std::string params = "pools=none:frame-threads=4:log-level=error:colorprim=" + std::to_string(decoded->color_primaries) + ":transfer=" + std::to_string(decoded->color_trc) + ":colormatrix=" + std::to_string(decoded->colorspace);
                if (r.crf == 0) params += ":lossless=1";
                if (decoded->color_trc == AVCOL_TRC_SMPTE2084)
                {
                    mastering = MasteringOption(decoded.get());
                    if (!mastering.empty()) params += ":master-display=" + mastering;
                }
                av_dict_set(&options, "x265-params", params.c_str(), 0);
            }
            const auto opened = avcodec_open2(c.encoder, encoder, &options);
            av_dict_free(&options); Check(opened, "open video encoder");
            targetStream = avformat_new_stream(c.output, nullptr);
            if (!targetStream) throw std::bad_alloc();
            targetStream->time_base = sourceStream->time_base;
            Check(avcodec_parameters_from_context(targetStream->codecpar, c.encoder), "output stream parameters");
            Check(avio_open2(&c.output->pb, r.output_path, AVIO_FLAG_WRITE, &c.output->interrupt_callback, nullptr), "open temporary output");
            Check(avformat_write_header(c.output, nullptr), "write container header");
            sourceFormat = decoded->format; sourceWidth = decoded->width; sourceHeight = decoded->height;
            matrix = decoded->colorspace; primaries = decoded->color_primaries; transfer = decoded->color_trc; range = decoded->color_range; chroma = decoded->chroma_location;
            color = std::make_unique<ColorPipeline>(decoded->colorspace, decoded->color_primaries, decoded->color_trc);
            upsampled = Allocate(AV_PIX_FMT_YUV444P16LE, sourceWidth, sourceHeight);
            composed = Allocate(AV_PIX_FMT_YUV444P16LE, r.width, r.height);
            outputFrame = Allocate(c.encoder->pix_fmt, r.width, r.height);
            c.upsample = MakeScaler(sourceWidth, sourceHeight, static_cast<AVPixelFormat>(sourceFormat), sourceWidth, sourceHeight, AV_PIX_FMT_YUV444P16LE, decoded->chroma_location, AVCHROMA_LOC_UNSPECIFIED, decoded->color_range == AVCOL_RANGE_JPEG);
            c.downsample = MakeScaler(r.width, r.height, AV_PIX_FMT_YUV444P16LE, r.width, r.height, c.encoder->pix_fmt, AVCHROMA_LOC_UNSPECIFIED, AVCHROMA_LOC_LEFT, decoded->color_range == AVCOL_RANGE_JPEG);
        }
        Need(sourceFormat == decoded->format && sourceWidth == decoded->width && sourceHeight == decoded->height &&
            matrix == decoded->colorspace && primaries == decoded->color_primaries && transfer == decoded->color_trc && range == decoded->color_range && chroma == decoded->chroma_location,
            "Midstream video format/color changes require a new export segment");
        if (decoded->color_trc == AVCOL_TRC_SMPTE2084)
            Need(MasteringOption(decoded.get()) == mastering, "Midstream mastering metadata changes require an explicit export policy");
        std::fill(layer.begin(), layer.end(), 0);
        const auto result = render(user, pts, sourceStream->time_base.num, sourceStream->time_base.den, r.width, r.height, layer.data(), layer.size());
        if (result != 0) throw Failure(result == 1 ? 4 : 3, result == 1 ? "Export cancelled" : "Project renderer failed");
        c.CheckCancel();
        Check(sws_scale(c.upsample, decoded->data, decoded->linesize, 0, decoded->height, upsampled->data, upsampled->linesize), "upsample encoded YUV");
        for (uint32_t y = 0; y < r.height; ++y)
        {
            if (!(y % 32)) c.CheckCancel();
            const uint16_t *input[3]{}; uint16_t *dest[3]{};
            for (int p = 0; p < 3; ++p)
            {
                input[p] = reinterpret_cast<uint16_t *>(upsampled->data[p] + (y + decoded->crop_top) * upsampled->linesize[p]) + decoded->crop_left;
                dest[p] = reinterpret_cast<uint16_t *>(composed->data[p] + y * composed->linesize[p]);
                std::memcpy(dest[p], input[p], r.width * 2);
            }
            for (uint32_t x = 0; x < r.width; ++x)
            {
                const auto *pixel = &layer[(static_cast<size_t>(y)*r.width+x)*4];
                if (pixel[3] == 0) continue;
                const bool limited = range == AVCOL_RANGE_MPEG;
                const double yOffset = limited ? 4096 : 0, yScale = limited ? 56064 : 65535, uvScale = limited ? 57344 : 65535;
                const auto encoded = color->Composite({(input[0][x]-yOffset)/yScale,(input[1][x]-32768.0)/uvScale,(input[2][x]-32768.0)/uvScale}, {pixel[0],pixel[1],pixel[2],pixel[3]}, r.reference_white_nits);
                for (int p = 0; p < 3; ++p)
                {
                    const auto value = encoded[p]*(p == 0 ? yScale : uvScale)+(p == 0 ? yOffset : 32768);
                    Need(std::isfinite(value), "Non-finite composited sample");
                    dest[p][x] = static_cast<uint16_t>(std::clamp(std::llround(value), 0LL, 65535LL));
                }
            }
        }
        Check(av_frame_make_writable(outputFrame.get()), "writable encode frame");
        Check(sws_scale(c.downsample, composed->data, composed->linesize, 0, r.height, outputFrame->data, outputFrame->linesize), "downsample encoded YUV");
        outputFrame->pts = pts; outputFrame->duration = decoded->duration; outputFrame->time_base = sourceStream->time_base;
        outputFrame->color_range = decoded->color_range; outputFrame->colorspace = decoded->colorspace;
        outputFrame->color_primaries = decoded->color_primaries; outputFrame->color_trc = decoded->color_trc;
        outputFrame->chroma_location = AVCHROMA_LOC_LEFT; outputFrame->sample_aspect_ratio = decoded->sample_aspect_ratio;
        Check(avcodec_send_frame(c.encoder, outputFrame.get()), "send composited frame");
        WritePackets(c, targetStream); ++frames;
    };
    auto receive = [&]()
    {
        while (true)
        {
            const auto result = avcodec_receive_frame(c.decoder, decoded.get());
            if (result == AVERROR_EOF || result == AVERROR(EAGAIN)) return;
            Check(result, "decode export frame"); process(); av_frame_unref(decoded.get());
        }
    };
    while (true)
    {
        c.CheckCancel(); const auto result = av_read_frame(c.input, c.packet);
        if (result == AVERROR_EOF) break;
        Check(result, "read source packet");
        if (c.packet->stream_index == r.video_stream_index)
        {
            auto sent = avcodec_send_packet(c.decoder, c.packet);
            if (sent == AVERROR(EAGAIN)) { receive(); sent = avcodec_send_packet(c.decoder, c.packet); }
            Check(sent, "send decode packet"); receive();
        }
        av_packet_unref(c.packet);
    }
    Check(avcodec_send_packet(c.decoder, nullptr), "drain decoder"); receive();
    Need(c.encoder != nullptr && frames > 0, "No decoded video frames");
    Check(avcodec_send_frame(c.encoder, nullptr), "drain encoder"); WritePackets(c, targetStream);
    Check(av_write_trailer(c.output), "write output trailer");
    Check(avio_closep(&c.output->pb), "close output file");
}
}
extern "C"
{
uint32_t AN_EXPORT_CALL an_export_abi_version(void) { return 1; }
int32_t AN_EXPORT_CALL an_export_create(void **context, char *error, uint32_t capacity)
{
    try
    {
        if (!context) throw Failure(1, "Missing context output");
        *context = nullptr; Versions(); auto result = std::make_unique<Context>();
        { std::scoped_lock lock(registryMutex); registry.insert(result.get()); }
        *context = result.release(); CopyError(error, capacity, ""); return 0;
    }
    catch (const Failure &e) { CopyError(error, capacity, e.what()); return e.code; }
    catch (const std::exception &e) { CopyError(error, capacity, e.what()); return 3; }
    catch (...) { CopyError(error, capacity, "Unknown native failure"); return 3; }
}
void AN_EXPORT_CALL an_export_cancel(void *context)
{
    try { Get(context)->cancelled.store(true); } catch (...) {}
}
void AN_EXPORT_CALL an_export_destroy(void *context)
{
    try
    {
        auto *value = static_cast<Context *>(context);
        { std::scoped_lock lock(registryMutex); if (!registry.erase(value)) return; }
        delete value;
    }
    catch (...) {}
}
int32_t AN_EXPORT_CALL an_export_run(void *context, const an_export_request *request,
    an_export_render_callback render, void *user, uint64_t *frames, char *error, uint32_t capacity)
{
    try
    {
        if (!request || request->struct_size != sizeof(*request) || request->abi_version != 1 || !render || !frames ||
            !request->input_path || !request->output_path || !request->preset || request->flags || request->reserved ||
            request->width == 0 || request->height == 0 || (request->width % 2) || (request->height % 2) ||
            static_cast<uint64_t>(request->width)*request->height > 33177600 || request->codec < 0 || request->codec > 2 ||
            request->crf < 0 || request->crf > 51 || !std::isfinite(request->reference_white_nits) || request->reference_white_nits <= 0)
            throw Failure(1, "Invalid export request ABI, dimensions, codec or reference white");
        *frames = 0; auto *value = Get(context); Execute(*value, *request, render, user, *frames);
        CopyError(error, capacity, ""); return 0;
    }
    catch (const Failure &e) { CopyError(error, capacity, e.what()); return e.code; }
    catch (const std::exception &e) { CopyError(error, capacity, e.what()); return 3; }
    catch (...) { CopyError(error, capacity, "Unknown native export failure"); return 3; }
}
}
