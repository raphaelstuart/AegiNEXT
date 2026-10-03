#include "aeginext_export.h"
#include <array>
#include <cstddef>
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
        static_assert(sizeof(an_export_request) == 64 && offsetof(an_export_request, input_path) == 32 &&
            offsetof(an_export_request, reference_white_nits) == 56);
        Require(an_export_abi_version() == 1, "ABI version mismatch");
        std::array<char, 256> error{};
        Require(an_export_create(nullptr, error.data(), error.size()) == 1, "Missing output accepted");
        an_export_destroy(nullptr); an_export_cancel(nullptr);
        for (int i = 0; i < 100; ++i)
        {
            void *context = nullptr;
            Require(an_export_create(&context, error.data(), error.size()) == 0 && context, "Context creation failed");
            uint64_t frames = 123;
            an_export_request request{sizeof(request),1,0,0,20,64,48,0,"unused","unused","ultrafast",203,0};
            request.abi_version = 2;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Invalid ABI was accepted");
            request.abi_version = 1;
            request.reserved = 1;
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Reserved field was accepted");
            request.reserved = 0;
            an_export_cancel(context);
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4 && frames == 0,
                "Sticky cancellation did not precede opening files");
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 4,
                "Cancellation was reset");
            an_export_destroy(context);
            an_export_destroy(context);
            Require(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()) == 1,
                "Destroyed handle was accepted");
        }
        std::cout << "PASS ABI, pinned runtime, invalid arguments, sticky cancellation, ownership\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
