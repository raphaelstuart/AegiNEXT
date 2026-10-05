#include "aeginext_export.h"
#include <cstdio>
#include <cstdlib>
#include <array>
int32_t AN_EXPORT_CALL Render(void *, int64_t, int32_t, int32_t, uint32_t w, uint32_t h, float *rgba, uint64_t)
{
    for (uint32_t y = 8; y < 16 && y < h; ++y)
        for (uint32_t x = 8; x < 16 && x < w; ++x)
            for (int c = 0; c < 4; ++c) rgba[(y*w+x)*4+c] = 0.5f;
    return 0;
}
int main(int argc, char **argv)
{
    if (argc < 3) return 2;
    void *context = nullptr;
    std::array<char, 1024> error{};
    auto result = an_export_create(&context, error.data(), error.size());
    if (result) { std::fprintf(stderr,"%s\n",error.data()); return result; }
    an_export_request request{sizeof(request),2,0,0,0,64,48,0,argv[1],argv[2],"ultrafast",203,0,
        argc > 3 ? std::atoi(argv[3]) : 0,8000000};
    uint64_t frames = 0;
    result = an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size());
    std::printf("result=%d frames=%llu encoder=%s error=%s\n",result,(unsigned long long)frames,an_export_encoder_name(context),error.data());
    an_export_destroy(context);
    return result;
}
