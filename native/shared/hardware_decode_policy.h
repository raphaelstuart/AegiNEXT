#pragma once
extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
}
namespace aeginext::media
{
const AVCodec *FindHardwareDecoder(AVCodecID codec, AVHWDeviceType device, AVPixelFormat format) noexcept;
const char *HardwareReadbackFailure(AVPixelFormat source, AVPixelFormat output) noexcept;
bool HardwareReadbackAlphaIsOpaque(const AVFrame *frame) noexcept;
bool IsHardwareSetupFailure(int result) noexcept;
#ifdef _WIN32
int CreateVulkanHardwareDevice(AVBufferRef **device);
#endif
}
