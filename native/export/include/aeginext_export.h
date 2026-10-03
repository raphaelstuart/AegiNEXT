#ifndef AEGINEXT_EXPORT_H
#define AEGINEXT_EXPORT_H
#include <stdint.h>
#if defined(_WIN32)
#define AN_EXPORT_CALL __cdecl
#ifdef AEGINEXT_EXPORT_BUILD
#define AN_EXPORT_API __declspec(dllexport)
#else
#define AN_EXPORT_API __declspec(dllimport)
#endif
#else
#define AN_EXPORT_CALL
#define AN_EXPORT_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* ABI 1; one run per context. Cancel may run concurrently; destruction may not. */
typedef struct an_export_request
{
    uint32_t struct_size, abi_version;
    int32_t video_stream_index, codec; /* 0 auto, 1 libx264 SDR, 2 libx265 10-bit */
    int32_t crf;
    uint32_t width, height, flags;
    const char *input_path, *output_path, *preset;
    float reference_white_nits;
    uint32_t reserved;
} an_export_request;
/* Fill tight premultiplied linear-sRGB float RGBA; return 0 OK, 1 cancelled, 2 error. */
typedef int32_t (AN_EXPORT_CALL *an_export_render_callback)(void *user, int64_t pts,
    int32_t time_base_num, int32_t time_base_den, uint32_t width, uint32_t height,
    float *rgba, uint64_t channels);
AN_EXPORT_API uint32_t AN_EXPORT_CALL an_export_abi_version(void);
AN_EXPORT_API int32_t AN_EXPORT_CALL an_export_create(void **context, char *error, uint32_t capacity);
AN_EXPORT_API void AN_EXPORT_CALL an_export_cancel(void *context);
AN_EXPORT_API void AN_EXPORT_CALL an_export_destroy(void *context);
/* 0 OK, 1 invalid argument, 2 unsupported, 3 failure, 4 cancelled. */
AN_EXPORT_API int32_t AN_EXPORT_CALL an_export_run(void *context, const an_export_request *request,
    an_export_render_callback render, void *user, uint64_t *frames, char *error, uint32_t capacity);
#ifdef __cplusplus
}
static_assert(sizeof(an_export_request) == 64);
#endif
#endif
