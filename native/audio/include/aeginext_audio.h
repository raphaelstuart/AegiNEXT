#pragma once
#include <stdint.h>
#ifdef _WIN32
#ifdef AEGINEXT_AUDIO_BUILD
#define AN_AUDIO_API __declspec(dllexport)
#else
#define AN_AUDIO_API __declspec(dllimport)
#endif
#else
#define AN_AUDIO_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
AN_AUDIO_API uint32_t an_audio_abi_version(void);
AN_AUDIO_API int an_audio_decoder_create(void **decoder, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_decoder_open(void *decoder, const char *path, int stream, int rate, int channels, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_decoder_read(void *decoder, float *samples, int frame_capacity, int *frames, int64_t *start_sample, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_decoder_seek(void *decoder, int64_t sample, char *error, uint32_t capacity);
AN_AUDIO_API void an_audio_decoder_cancel(void *decoder);
AN_AUDIO_API void an_audio_decoder_destroy(void *decoder);
AN_AUDIO_API int an_audio_output_create(void **output, int rate, int channels, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_output_write(void *output, const float *samples, int frames, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_output_pause(void *output, int pause, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_output_clear(void *output, char *error, uint32_t capacity);
AN_AUDIO_API int an_audio_output_queued(void *output);
AN_AUDIO_API int an_audio_output_latency(void *output);
AN_AUDIO_API int an_audio_output_gain(void *output, float gain, char *error, uint32_t capacity);
AN_AUDIO_API void an_audio_output_destroy(void *output);
#ifdef __cplusplus
}
#endif
