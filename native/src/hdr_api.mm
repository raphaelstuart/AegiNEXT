#include "aeginext_hdr.h"
#include "hdr_context.h"
#include "hdr_error.h"

#import <AppKit/AppKit.h>
#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <exception>
#include <memory>
#include <type_traits>
#include <unordered_set>

static_assert(std::is_standard_layout_v<an_hdr_status>);
static_assert(sizeof(an_hdr_status) == 72);
static_assert(alignof(an_hdr_status) == 8);
static_assert(offsetof(an_hdr_status, current_headroom) == 32);
static_assert(offsetof(an_hdr_status, submitted_frames) == 48);
static_assert(offsetof(an_hdr_status, source_white_nits) == 56);
static_assert(offsetof(an_hdr_status, live_contexts) == 64);
static_assert(std::is_standard_layout_v<an_hdr_verification>);
static_assert(sizeof(an_hdr_verification) == 32);
static_assert(offsetof(an_hdr_verification, upload_max_error) == 16);

namespace
{
std::atomic<uint32_t> liveContexts{0};
std::unordered_set<AegiNext::HdrContext *> contexts;

void WriteError(char *error, uint32_t capacity, const char *message) noexcept
{
    if (error && capacity > 0)
    {
        const auto text = message ? message : "Unknown native HDR failure.";
        const auto length = std::strlen(text);
        auto copied = std::min(length, static_cast<size_t>(capacity - 1));
        if (copied < length)
        {
            while (copied > 0 && (static_cast<unsigned char>(text[copied]) & 0xc0) == 0x80)
            {
                --copied;
            }
        }

        std::memcpy(error, text, copied);
        error[copied] = '\0';
    }
}

template<typename Action>
int32_t Boundary(char *error, uint32_t capacity, Action &&action) noexcept
{
    @autoreleasepool
    {
        @try
        {
            try
            {
                if (capacity > 0 && !error)
                {
                    return AN_HDR_INVALID_ARGUMENT;
                }

                WriteError(error, capacity, "");
                if (![NSThread isMainThread])
                {
                    WriteError(error, capacity, "This native HDR operation must run on the macOS main thread.");
                    return AN_HDR_WRONG_THREAD;
                }

                action();
                return AN_HDR_OK;
            }
            catch (const AegiNext::HdrError &exception)
            {
                WriteError(error, capacity, exception.what());
                return exception.Code();
            }
            catch (const std::exception &exception)
            {
                WriteError(error, capacity, exception.what());
                return AN_HDR_NATIVE_FAILURE;
            }
            catch (...)
            {
                WriteError(error, capacity, "An unknown C++ exception occurred in the native HDR backend.");
                return AN_HDR_NATIVE_FAILURE;
            }
        }
        @catch (NSException *exception)
        {
            WriteError(error, capacity, exception.reason.UTF8String);
            return AN_HDR_NATIVE_FAILURE;
        }
    }
}

AegiNext::HdrContext &GetContext(void *handle)
{
    auto context = static_cast<AegiNext::HdrContext *>(handle);
    if (!context || !contexts.contains(context))
    {
        throw AegiNext::HdrError(AN_HDR_INVALID_ARGUMENT, "The native HDR context handle is not live.");
    }

    return *context;
}

template<typename Value>
void ResetOutput(Value *value)
{
    if (!value || value->struct_size != sizeof(Value) || value->abi_version != AN_HDR_ABI_VERSION)
    {
        throw AegiNext::HdrError(AN_HDR_INVALID_ARGUMENT, "The output struct size or ABI version does not match the native HDR library.");
    }

    *value = {};
    value->struct_size = sizeof(Value);
    value->abi_version = AN_HDR_ABI_VERSION;
}

void DestroyOnMain(void *handle) noexcept
{
    @autoreleasepool
    {
        @try
        {
            try
            {
                auto context = static_cast<AegiNext::HdrContext *>(handle);
                if (contexts.erase(context) == 0)
                {
                    return;
                }

                delete context;
                liveContexts.fetch_sub(1, std::memory_order_release);
            }
            catch (...)
            {
                std::fputs("AegiNext: exception while destroying an HDR context.\n", stderr);
            }
        }
        @catch (NSException *exception)
        {
            std::fprintf(stderr, "AegiNext: HDR context destruction exception: %s\n", exception.reason.UTF8String);
        }
    }
}
}

uint32_t an_hdr_abi_version(void)
{
    return AN_HDR_ABI_VERSION;
}

uint32_t an_hdr_live_contexts(void)
{
    return liveContexts.load(std::memory_order_acquire);
}

int32_t an_hdr_create(void **context, void **nsview, char *error, uint32_t capacity)
{
    return Boundary(error, capacity, [&]
    {
        if (!context || !nsview || context == nsview)
        {
            throw AegiNext::HdrError(AN_HDR_INVALID_ARGUMENT, "Distinct context and NSView output pointers are required.");
        }

        *context = nullptr;
        *nsview = nullptr;
        auto created = std::make_unique<AegiNext::HdrContext>();
        created->Initialize();
        contexts.insert(created.get());
        liveContexts.fetch_add(1, std::memory_order_release);
        *nsview = created->View();
        *context = created.release();
    });
}

void an_hdr_destroy(void *context)
{
    if (!context)
    {
        return;
    }

    @try
    {
        if ([NSThread isMainThread])
        {
            DestroyOnMain(context);
        }
        else
        {
            dispatch_async(dispatch_get_main_queue(), ^
            {
                DestroyOnMain(context);
            });
        }
    }
    @catch (NSException *exception)
    {
        std::fprintf(stderr, "AegiNext: could not schedule HDR destruction: %s\n", exception.reason.UTF8String);
    }
}

int32_t an_hdr_present(
    void *context, const uint16_t *rgba, uint64_t byte_count,
    uint32_t width, uint32_t height, uint32_t row_bytes,
    float source_white_nits, float source_peak_nits,
    an_hdr_status *status, char *error, uint32_t capacity)
{
    return Boundary(error, capacity, [&]
    {
        ResetOutput(status);
        auto &nativeContext = GetContext(context);
        status->live_contexts = an_hdr_live_contexts();
        nativeContext.Present(rgba, byte_count, width, height, row_bytes,
                              source_white_nits, source_peak_nits, *status);
    });
}

int32_t an_hdr_verify(void *context, an_hdr_verification *result, char *error, uint32_t capacity)
{
    return Boundary(error, capacity, [&]
    {
        ResetOutput(result);
        GetContext(context).Verify(*result);
    });
}
