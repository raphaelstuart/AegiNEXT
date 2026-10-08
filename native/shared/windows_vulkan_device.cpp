#define NOMINMAX
#include <windows.h>
#include <dxgi1_2.h>
#include "hardware_decode_policy.h"
#include <memory>
#include <string>
namespace aeginext::media
{
namespace
{
struct ComRelease
{
    void operator()(IUnknown *value) const noexcept { if (value) { value->Release(); } }
};
}
int CreateVulkanHardwareDevice(AVBufferRef **device)
{
    // Select an adapter exposed by Windows as hardware by its Vulkan device name.
    // FFmpeg's default Vulkan device index could select a CPU implementation.
    IDXGIFactory1 *rawFactory = nullptr;
    const auto created = CreateDXGIFactory1(__uuidof(IDXGIFactory1), reinterpret_cast<void **>(&rawFactory));
    if (created == E_OUTOFMEMORY) { return AVERROR(ENOMEM); }
    if (FAILED(created)) { return AVERROR(ENODEV); }
    const std::unique_ptr<IDXGIFactory1, ComRelease> factory(rawFactory);
    int lastFailure = AVERROR(ENODEV);
    for (UINT index = 0; ; ++index)
    {
        IDXGIAdapter1 *rawAdapter = nullptr;
        const auto enumerated = factory->EnumAdapters1(index, &rawAdapter);
        if (enumerated == DXGI_ERROR_NOT_FOUND) { break; }
        if (enumerated == E_OUTOFMEMORY) { return AVERROR(ENOMEM); }
        if (FAILED(enumerated)) { return AVERROR(ENODEV); }
        const std::unique_ptr<IDXGIAdapter1, ComRelease> adapter(rawAdapter);
        DXGI_ADAPTER_DESC1 description{};
        if (FAILED(adapter->GetDesc1(&description)) || (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) { continue; }
        const auto size = WideCharToMultiByte(CP_UTF8, 0, description.Description, -1, nullptr, 0, nullptr, nullptr);
        if (size <= 1) { continue; }
        std::string name(static_cast<size_t>(size), '\0');
        if (!WideCharToMultiByte(CP_UTF8, 0, description.Description, -1, name.data(), size, nullptr, nullptr)) { continue; }
        const auto result = av_hwdevice_ctx_create(device, AV_HWDEVICE_TYPE_VULKAN, name.c_str(), nullptr, 0);
        if (result >= 0 || result == AVERROR(ENOMEM)) { return result; }
        av_buffer_unref(device);
        lastFailure = result;
    }
    return lastFailure;
}
}
