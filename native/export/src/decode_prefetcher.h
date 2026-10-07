#pragma once
#include "decode_prefetch_statistics.h"
#include "export_decoded_frame.h"
#include <condition_variable>
#include <exception>
#include <functional>
#include <mutex>
#include <optional>
#include <thread>

namespace aeginext::encode
{
class DecodePrefetcher final
{
public:
    DecodePrefetcher(std::function<ExportDecodedFrame()> producer, std::function<void()> interrupt);
    ~DecodePrefetcher();
    DecodePrefetcher(const DecodePrefetcher &) = delete;
    DecodePrefetcher &operator=(const DecodePrefetcher &) = delete;
    ExportDecodedFrame Next();
    void RequestStop() noexcept;
    void Stop();
    DecodePrefetchStatistics Statistics() const;

private:
    void Work();

    std::function<ExportDecodedFrame()> producer_;
    std::function<void()> interrupt_;
    mutable std::mutex mutex_;
    std::condition_variable available_;
    std::condition_variable space_;
    std::optional<ExportDecodedFrame> queued_;
    std::exception_ptr error_;
    bool stopping_ = false;
    bool finished_ = false;
    DecodePrefetchStatistics statistics_;
    std::thread worker_;
};
}
