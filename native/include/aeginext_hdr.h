#ifndef AEGINEXT_HDR_H
#define AEGINEXT_HDR_H

#include <stdint.h>

#if defined(_WIN32)
#define AN_HDR_API __declspec(dllexport)
#else
#define AN_HDR_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C"
{
#endif

enum an_hdr_result
{
    AN_HDR_OK = 0,
    AN_HDR_INVALID_ARGUMENT = 1,
    AN_HDR_UNSUPPORTED = 2,
    AN_HDR_NATIVE_FAILURE = 3,
    AN_HDR_WRONG_THREAD = 4,
    AN_HDR_NOT_READY = 5
};

enum
{
    AN_HDR_ABI_VERSION = 1
};

typedef struct an_hdr_status
{
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t drawable_width;
    uint32_t drawable_height;
    uint32_t float16_verified;
    uint32_t edr_enabled;
    uint32_t display_id;
    uint32_t reserved;
    float current_headroom;
    float potential_headroom;
    float backing_scale;
    float nominal_display_white;
    uint64_t submitted_frames;
    float source_white_nits;
    float source_peak_nits;
    uint32_t live_contexts;
    uint32_t reserved2;
} an_hdr_status;

typedef struct an_hdr_verification
{
    uint32_t struct_size;
    uint32_t abi_version;
    uint32_t pipeline_ok;
    uint32_t reserved;
    float upload_max_error;
    float primaries_max_error;
    float reference_white_max_error;
    float hdr_max_component;
} an_hdr_verification;

AN_HDR_API uint32_t an_hdr_abi_version(void);
AN_HDR_API uint32_t an_hdr_live_contexts(void);

/* Main-thread APIs. The returned NSView is borrowed from the context; the host
 * attaches and sizes it. Errors are UTF-8, NUL-terminated when capacity > 0. */
AN_HDR_API int32_t an_hdr_create(void **context, void **nsview, char *error, uint32_t capacity);

/* Synchronous on the main thread. A call from another thread queues destruction
 * on the main queue; the context must not be used again after either call. */
AN_HDR_API void an_hdr_destroy(void *context);

/* Input is little-endian, premultiplied RGBA binary16 in linear BT.709/sRGB.
 * RGB 1.0 represents source_white_nits. source_peak_nits describes the largest
 * unassociated RGB component's luminance, and must cover the submitted content.
 * The buffer is borrowed only until this function returns, including GPU upload.
 * status must contain its exact struct_size and AN_HDR_ABI_VERSION on entry.
 * EDR output is relative to the current display white, not calibrated nits. */
AN_HDR_API int32_t an_hdr_present(
    void *context,
    const uint16_t *rgba,
    uint64_t byte_count,
    uint32_t width,
    uint32_t height,
    uint32_t row_bytes,
    float source_white_nits,
    float source_peak_nits,
    an_hdr_status *status,
    char *error,
    uint32_t capacity);

/* Actual GPU upload/readback and offscreen libplacebo conversion. This does not
 * read back the swapchain and does not certify physical display luminance.
 * result must contain its exact struct_size and AN_HDR_ABI_VERSION on entry. */
AN_HDR_API int32_t an_hdr_verify(
    void *context,
    an_hdr_verification *result,
    char *error,
    uint32_t capacity);

#ifdef __cplusplus
}
#endif

#endif
