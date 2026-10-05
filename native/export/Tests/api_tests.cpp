#include "aeginext_export.h"
#include <array>
#include <cstddef>
#include <cstring>
#include <iostream>
#include <stdexcept>
void Require(bool value, const char *message)
{
    if (!value) throw std::runtime_error(message);
}
int32_t AN_EXPORT_CALL Render(void *, int64_t, int32_t, int32_t, uint32_t, uint32_t, float *, uint64_t)
{
    return 0;
}
int main()
{
    try
    {
        static_assert(sizeof(an_export_request) == 80 && offsetof(an_export_request, input_path) == 32 &&
            offsetof(an_export_request, reference_white_nits) == 56 && offsetof(an_export_request, encoding_mode) == 64);
        Require(an_export_abi_version() == 3, "ABI version mismatch");
        Require(an_export_core_version() == 1 && (an_export_capabilities() & 7) == 7,
            "Shared media core version or capabilities are missing");
        std::array<char, 256> error{};
        Require(an_export_create(nullptr, error.data(), error.size()) == 1, "Missing output accepted");
        an_export_destroy(nullptr); an_export_cancel(nullptr);
        for (int i = 0; i < 100; ++i)
        {
            void *context = nullptr;
            Require(an_export_create(&context, error.data(), error.size()) == 0 && context, "Context creation failed");
            uint64_t frames = 123;
            an_export_request request{sizeof(request),3,0,0,20,64,48,0,"unused","unused","ultrafast",203,0,0,8000000,0,0};
            an_export_result_info info{};
            info.struct_size = sizeof(info);
            info.abi_version = 3;
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Result information reported before completion");
            Require(an_export_get_result_info(context, nullptr, error.data(), error.size()) == 1,
                "Missing result information output accepted");
            info.struct_size = 0;
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Invalid result information layout accepted");
            info.struct_size = sizeof(info);
            Require(std::strlen(an_export_encoder_name(context)) == 0, "Encoder reported before initialization");
            request.abi_version = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Invalid ABI was accepted");
            request.abi_version = 3;
            request.reserved = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Reserved field was accepted");
            request.reserved = 0;
            request.decode_mode = 3;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Unknown decode mode accepted");
            request.decode_mode = 0;
            request.decode_reserved = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Decode reserved field was accepted");
            request.decode_reserved = 0;
            request.encoding_mode = 2;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Unknown encoding mode accepted");
            request.encoding_mode = 1;
            request.video_bitrate = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Invalid hardware bitrate accepted");
            request.video_bitrate = 8000000;
            an_export_cancel(context);
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4 && frames == 0,
                "Sticky cancellation did not precede opening files");
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                "Cancellation was reset");
            an_export_destroy(context);
            an_export_destroy(context);
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Destroyed handle was accepted");
            Require(an_export_get_result_info(context, &info, error.data(), error.size()) == 1,
                "Destroyed result information handle accepted");
        }
        std::cout << "PASS ABI 3, encoding mode and bitrate, pinned runtime, invalid arguments, sticky cancellation, ownership\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
