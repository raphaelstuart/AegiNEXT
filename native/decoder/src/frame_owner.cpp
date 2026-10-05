#include "frame_owner.h"
#include <cstring>
#include <limits>

extern "C"
{
#include <libavutil/buffer.h>
#include <libavutil/imgutils.h>
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/pixdesc.h>
}

namespace aeginext::decode
{
namespace
{
an_decode_ratio Ratio(AVRational value)
{
    if (value.den <= 0)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Present HDR metadata contains an invalid rational denominator.");
    }

    return {value.num, value.den};
}
}

FrameOwner::FrameOwner(FramePointer frame, AVRational streamTimeBase, aeginext::media::SourceColorContext colorContext) : frame_(std::move(frame)), colorContext_(colorContext)
{
    if (!frame_ || frame_->width <= 0 || frame_->height <= 0 || streamTimeBase.num <= 0 || streamTimeBase.den <= 0)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Invalid decoded frame dimensions or stream time base.");
    }

    const auto format = static_cast<AVPixelFormat>(frame_->format);
    const auto *descriptor = av_pix_fmt_desc_get(format);
    if (!descriptor || (descriptor->flags & AV_PIX_FMT_FLAG_HWACCEL) != 0 || descriptor->nb_components > 4)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Decoder did not return a supported software pixel format.");
    }

    if (frame_->crop_left >= static_cast<size_t>(frame_->width) ||
        frame_->crop_right >= static_cast<size_t>(frame_->width) - frame_->crop_left ||
        frame_->crop_top >= static_cast<size_t>(frame_->height) ||
        frame_->crop_bottom >= static_cast<size_t>(frame_->height) - frame_->crop_top ||
        frame_->nb_side_data < 0 || frame_->duration < 0)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Invalid decoded frame crop, duration or side-data count.");
    }

    info_.struct_size = sizeof(info_);
    info_.abi_version = AN_DECODE_ABI_VERSION;
    info_.width = static_cast<uint32_t>(frame_->width);
    info_.height = static_cast<uint32_t>(frame_->height);
    info_.component_count = descriptor->nb_components;
    info_.decode_error_flags = static_cast<uint32_t>(frame_->decode_error_flags);
    info_.pixel_format = frame_->format;
    const auto frameBase = frame_->time_base;
    const auto ptsBase = frameBase.num > 0 && frameBase.den > 0 ? frameBase : streamTimeBase;
    info_.time_base_num = ptsBase.num;
    info_.time_base_den = ptsBase.den;
    info_.raw_frame_time_base_num = frameBase.num;
    info_.raw_frame_time_base_den = frameBase.den;
    info_.stream_time_base_num = streamTimeBase.num;
    info_.stream_time_base_den = streamTimeBase.den;
    info_.sample_aspect_ratio_num = frame_->sample_aspect_ratio.num;
    info_.sample_aspect_ratio_den = frame_->sample_aspect_ratio.den;
    info_.crop_left = static_cast<uint32_t>(frame_->crop_left);
    info_.crop_top = static_cast<uint32_t>(frame_->crop_top);
    info_.crop_right = static_cast<uint32_t>(frame_->crop_right);
    info_.crop_bottom = static_cast<uint32_t>(frame_->crop_bottom);
    for (uint32_t index = 0; index < info_.component_count; ++index)
    {
        info_.component_depth[index] = static_cast<uint32_t>(descriptor->comp[index].depth);
    }

    info_.color_range = frame_->color_range;
    info_.color_matrix = frame_->colorspace;
    info_.color_primaries = frame_->color_primaries;
    info_.color_transfer = frame_->color_trc;
    info_.chroma_location = frame_->chroma_location;
    info_.alpha_mode = frame_->alpha_mode;
    info_.side_data_count = static_cast<uint32_t>(frame_->nb_side_data);
    if (frame_->pts != AV_NOPTS_VALUE)
    {
        info_.flags |= AN_FRAME_HAS_PTS;
        info_.pts = frame_->pts;
    }

    if (frame_->best_effort_timestamp != AV_NOPTS_VALUE)
    {
        info_.flags |= AN_FRAME_HAS_BEST_EFFORT_TIMESTAMP;
        info_.best_effort_timestamp = frame_->best_effort_timestamp;
    }

    if (frame_->duration > 0)
    {
        info_.flags |= AN_FRAME_HAS_DURATION;
        info_.duration = frame_->duration;
    }

    if (frame_->flags & AV_FRAME_FLAG_KEY) { info_.flags |= AN_FRAME_KEY; }
    if (frame_->flags & AV_FRAME_FLAG_CORRUPT) { info_.flags |= AN_FRAME_CORRUPT; }
    if (frame_->flags & AV_FRAME_FLAG_INTERLACED) { info_.flags |= AN_FRAME_INTERLACED; }
    if (frame_->flags & AV_FRAME_FLAG_TOP_FIELD_FIRST) { info_.flags |= AN_FRAME_TOP_FIELD_FIRST; }

    CopyName(info_.pixel_format_name, AN_DECODE_NAME_CAPACITY, descriptor->name);
    CopyName(info_.color_range_name, AN_DECODE_NAME_CAPACITY, av_color_range_name(frame_->color_range));
    CopyName(info_.color_matrix_name, AN_DECODE_NAME_CAPACITY, av_color_space_name(frame_->colorspace));
    CopyName(info_.color_primaries_name, AN_DECODE_NAME_CAPACITY, av_color_primaries_name(frame_->color_primaries));
    CopyName(info_.color_transfer_name, AN_DECODE_NAME_CAPACITY, av_color_transfer_name(frame_->color_trc));
    CopyName(info_.chroma_location_name, AN_DECODE_NAME_CAPACITY, av_chroma_location_name(frame_->chroma_location));
    CopyName(info_.alpha_mode_name, AN_DECODE_NAME_CAPACITY, av_alpha_mode_name(frame_->alpha_mode));
    ReadPlanes(descriptor);
    ReadMetadata();
}

void FrameOwner::ReadPlanes(const AVPixFmtDescriptor *descriptor)
{
    int rowBytes[4]{};
    ptrdiff_t tightStrides[4]{};
    size_t planeSizes[4]{};
    const auto format = static_cast<AVPixelFormat>(frame_->format);
    CheckAv(av_image_fill_linesizes(rowBytes, format, frame_->width), AN_DECODE_UNSUPPORTED, "av_image_fill_linesizes");
    for (size_t index = 0; index < 4; ++index)
    {
        tightStrides[index] = rowBytes[index];
    }

    CheckAv(av_image_fill_plane_sizes(planeSizes, format, frame_->height, tightStrides),
        AN_DECODE_UNSUPPORTED, "av_image_fill_plane_sizes");
    for (uint32_t index = 0; index < 4; ++index)
    {
        if (planeSizes[index] == 0)
        {
            continue;
        }

        if (index != info_.plane_count)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Noncontiguous video plane indices are not supported by this ABI.");
        }

        const auto isPalette = (descriptor->flags & AV_PIX_FMT_FLAG_PAL) != 0 && index == 1;
        if (isPalette)
        {
            rowBytes[index] = 1024;
        }

        if (rowBytes[index] <= 0 || planeSizes[index] % static_cast<size_t>(rowBytes[index]) != 0)
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Pixel plane does not have a representable row layout.");
        }

        const auto rows = planeSizes[index] / static_cast<size_t>(rowBytes[index]);
        if (rows == 0 || rows > std::numeric_limits<uint32_t>::max())
        {
            throw Error(AN_DECODE_UNSUPPORTED, "Pixel plane row count exceeds the ABI range.");
        }

        auto &plane = planes_[index];
        plane.struct_size = sizeof(plane);
        plane.abi_version = AN_DECODE_ABI_VERSION;
        plane.plane_index = index;
        plane.rows = static_cast<uint32_t>(rows);
        plane.native_stride = frame_->linesize[index];
        plane.row_bytes = static_cast<uint32_t>(rowBytes[index]);
        plane.tight_byte_count = planeSizes[index];
        ++info_.plane_count;
        ValidatePlane(index);
    }

    if (info_.plane_count == 0)
    {
        throw Error(AN_DECODE_UNSUPPORTED, "Decoded frame contains no accessible pixel planes.");
    }
}

void FrameOwner::ValidatePlane(uint32_t index) const
{
    const auto &plane = planes_[index];
    auto *buffer = av_frame_get_plane_buffer(frame_.get(), static_cast<int>(index));
    if (!frame_->data[index] || !buffer)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "A pixel plane is not backed by an owned AVBufferRef.");
    }

    const auto stride = static_cast<int64_t>(plane.native_stride);
    const auto absoluteStride = stride < 0 ? -stride : stride;
    if (plane.rows > 1 && static_cast<uint64_t>(absoluteStride) < plane.row_bytes)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Pixel stride is smaller than an active row.");
    }

    const auto bufferStart = reinterpret_cast<uintptr_t>(buffer->data);
    const auto planeStart = reinterpret_cast<uintptr_t>(frame_->data[index]);
    if (planeStart < bufferStart || planeStart - bufferStart > buffer->size)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Pixel plane starts outside its owned buffer.");
    }

    const auto origin = static_cast<uint64_t>(planeStart - bufferStart);
    const auto lastOffset = stride * static_cast<int64_t>(plane.rows - 1);
    if ((lastOffset < 0 && static_cast<uint64_t>(-lastOffset) > origin) ||
        (lastOffset >= 0 && static_cast<uint64_t>(lastOffset) > buffer->size - origin))
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Pixel stride leaves the owned buffer.");
    }

    const auto maximumOffset = origin + (lastOffset > 0 ? static_cast<uint64_t>(lastOffset) : 0);
    if (plane.row_bytes > buffer->size - maximumOffset)
    {
        throw Error(AN_DECODE_DECODE_ERROR, "Pixel row extends past the owned buffer.");
    }
}

void FrameOwner::ReadMetadata()
{
    hdr_.struct_size = sizeof(hdr_);
    hdr_.abi_version = AN_DECODE_ABI_VERSION;
    if (const auto *sideData = av_frame_get_side_data(frame_.get(), AV_FRAME_DATA_MASTERING_DISPLAY_METADATA))
    {
        if (sideData->size < sizeof(AVMasteringDisplayMetadata))
        {
            throw Error(AN_DECODE_DECODE_ERROR, "Mastering display metadata is truncated.");
        }

        const auto *metadata = reinterpret_cast<const AVMasteringDisplayMetadata *>(sideData->data);
        hdr_.flags |= AN_HDR_MASTERING_PRESENT;
        if (metadata->has_primaries)
        {
            hdr_.flags |= AN_HDR_MASTERING_HAS_PRIMARIES;
            hdr_.red_x = Ratio(metadata->display_primaries[0][0]);
            hdr_.red_y = Ratio(metadata->display_primaries[0][1]);
            hdr_.green_x = Ratio(metadata->display_primaries[1][0]);
            hdr_.green_y = Ratio(metadata->display_primaries[1][1]);
            hdr_.blue_x = Ratio(metadata->display_primaries[2][0]);
            hdr_.blue_y = Ratio(metadata->display_primaries[2][1]);
            hdr_.white_point_x = Ratio(metadata->white_point[0]);
            hdr_.white_point_y = Ratio(metadata->white_point[1]);
        }

        if (metadata->has_luminance)
        {
            hdr_.flags |= AN_HDR_MASTERING_HAS_LUMINANCE;
            hdr_.min_luminance = Ratio(metadata->min_luminance);
            hdr_.max_luminance = Ratio(metadata->max_luminance);
        }
    }

    if (const auto *sideData = av_frame_get_side_data(frame_.get(), AV_FRAME_DATA_CONTENT_LIGHT_LEVEL))
    {
        if (sideData->size < sizeof(AVContentLightMetadata))
        {
            throw Error(AN_DECODE_DECODE_ERROR, "Content light metadata is truncated.");
        }

        const auto *metadata = reinterpret_cast<const AVContentLightMetadata *>(sideData->data);
        hdr_.flags |= AN_HDR_CONTENT_LIGHT_PRESENT;
        hdr_.max_content_light_level = metadata->MaxCLL;
        hdr_.max_frame_average_light_level = metadata->MaxFALL;
    }
}

an_frame_plane_info FrameOwner::Plane(uint32_t index) const
{
    if (index >= info_.plane_count)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "Plane index is out of range.");
    }

    return planes_[index];
}

const char *FrameOwner::SideDataName(uint32_t index) const
{
    if (index >= info_.side_data_count)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "Side-data index is out of range.");
    }

    const auto *name = av_frame_side_data_name(frame_->side_data[index]->type);
    return name ? name : "Unknown side data";
}

void FrameOwner::CopyPlane(uint32_t index, uint8_t *destination, uint64_t capacity) const
{
    const auto plane = Plane(index);
    if (!destination || capacity < plane.tight_byte_count)
    {
        throw Error(AN_DECODE_INVALID_ARGUMENT, "Destination cannot hold all active pixel rows.");
    }

    for (uint32_t row = 0; row < plane.rows; ++row)
    {
        const auto offset = static_cast<int64_t>(row) * plane.native_stride;
        std::memcpy(destination + static_cast<size_t>(row) * plane.row_bytes,
            frame_->data[index] + offset, plane.row_bytes);
    }
}
}
